using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FeedTriage.Worker.Configuration;
using FeedTriage.Worker.Interfaces;
using FeedTriage.Worker.Models;

namespace FeedTriage.Worker.Services;

public sealed class SqliteArticleScoreRepository : IArticleScoreRepository
{
    private const string LastDailyStarringRunKey = "last_daily_starring_run_utc";

    private readonly string _databasePath;
    private readonly ILogger<SqliteArticleScoreRepository> _logger;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private volatile bool _initialized;

    public SqliteArticleScoreRepository(
        IOptions<StateOptions> options,
        ILogger<SqliteArticleScoreRepository> logger)
    {
        _databasePath = options.Value.ScoreDatabasePath;
        _logger = logger;
    }

    public async Task SaveScoreAsync(StoredArticleScore score, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var connection = CreateConnection();
        await connection.OpenAsync(ct);
        await EnableForeignKeysAsync(connection, ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        var now = DateTimeOffset.UtcNow.ToString("O");
        var scoreDate = score.ScoreDate.ToString("yyyy-MM-dd");

        var upsertCommand = connection.CreateCommand();
        upsertCommand.Transaction = transaction;
        upsertCommand.CommandText =
            """
            INSERT INTO article_scores (score_date, entry_id, title, url, total_score, created_at_utc, updated_at_utc)
            VALUES ($scoreDate, $entryId, $title, $url, $totalScore, $createdAtUtc, $updatedAtUtc)
            ON CONFLICT(score_date, entry_id) DO UPDATE SET
                title = excluded.title,
                url = excluded.url,
                total_score = excluded.total_score,
                updated_at_utc = excluded.updated_at_utc;
            """;
        upsertCommand.Parameters.AddWithValue("$scoreDate", scoreDate);
        upsertCommand.Parameters.AddWithValue("$entryId", score.EntryId);
        upsertCommand.Parameters.AddWithValue("$title", score.Title);
        upsertCommand.Parameters.AddWithValue("$url", score.Url);
        upsertCommand.Parameters.AddWithValue("$totalScore", score.TotalScore);
        upsertCommand.Parameters.AddWithValue("$createdAtUtc", now);
        upsertCommand.Parameters.AddWithValue("$updatedAtUtc", now);
        await upsertCommand.ExecuteNonQueryAsync(ct);

        var deleteTopicsCommand = connection.CreateCommand();
        deleteTopicsCommand.Transaction = transaction;
        deleteTopicsCommand.CommandText =
            """
            DELETE FROM article_topic_scores
            WHERE score_date = $scoreDate AND entry_id = $entryId;
            """;
        deleteTopicsCommand.Parameters.AddWithValue("$scoreDate", scoreDate);
        deleteTopicsCommand.Parameters.AddWithValue("$entryId", score.EntryId);
        await deleteTopicsCommand.ExecuteNonQueryAsync(ct);

        foreach (var (topic, topicScore) in score.TopicScores)
        {
            var insertTopicCommand = connection.CreateCommand();
            insertTopicCommand.Transaction = transaction;
            insertTopicCommand.CommandText =
                """
                INSERT INTO article_topic_scores (score_date, entry_id, topic, score)
                VALUES ($scoreDate, $entryId, $topic, $score);
                """;
            insertTopicCommand.Parameters.AddWithValue("$scoreDate", scoreDate);
            insertTopicCommand.Parameters.AddWithValue("$entryId", score.EntryId);
            insertTopicCommand.Parameters.AddWithValue("$topic", topic);
            insertTopicCommand.Parameters.AddWithValue("$score", Math.Clamp(topicScore, 0, 5));
            await insertTopicCommand.ExecuteNonQueryAsync(ct);
        }

        await transaction.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<DateOnly>> GetScoredDatesAsync(
        DateOnly fromInclusive,
        DateOnly toInclusive,
        CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        if (toInclusive < fromInclusive)
        {
            return [];
        }

        await using var connection = CreateConnection();
        await connection.OpenAsync(ct);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT DISTINCT score_date
            FROM article_scores
            WHERE score_date >= $fromDate AND score_date <= $toDate
            ORDER BY score_date ASC;
            """;
        command.Parameters.AddWithValue("$fromDate", fromInclusive.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$toDate", toInclusive.ToString("yyyy-MM-dd"));

        var dates = new List<DateOnly>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            if (DateOnly.TryParse(reader.GetString(0), out var scoreDate))
            {
                dates.Add(scoreDate);
            }
        }

        return dates;
    }

    public async Task<IReadOnlyList<StoredArticleScore>> GetTopScoresAsync(
        DateOnly scoreDate,
        int take,
        int minimumTotalScore,
        CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        if (take <= 0)
        {
            return [];
        }

        await using var connection = CreateConnection();
        await connection.OpenAsync(ct);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT score_date, entry_id, title, url, total_score
            FROM article_scores
            WHERE score_date = $scoreDate AND total_score >= $minimumTotalScore
            ORDER BY total_score DESC, entry_id ASC
            LIMIT $take;
            """;
        command.Parameters.AddWithValue("$scoreDate", scoreDate.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$minimumTotalScore", minimumTotalScore);
        command.Parameters.AddWithValue("$take", take);

        var scores = new List<StoredArticleScore>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var entryId = reader.GetInt64(1);
            scores.Add(new StoredArticleScore
            {
                ScoreDate = scoreDate,
                EntryId = entryId,
                Title = reader.GetString(2),
                Url = reader.GetString(3),
                TotalScore = reader.GetInt32(4),
                TopicScores = await LoadTopicScoresAsync(connection, scoreDate, entryId, ct)
            });
        }

        return scores;
    }

    public async Task<DateTimeOffset?> GetLastDailyStarringRunAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var connection = CreateConnection();
        await connection.OpenAsync(ct);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM metadata WHERE key = $key LIMIT 1;";
        command.Parameters.AddWithValue("$key", LastDailyStarringRunKey);

        var value = await command.ExecuteScalarAsync(ct);
        if (value is string text && DateTimeOffset.TryParse(text, out var runAt))
        {
            return runAt;
        }

        return null;
    }

    public async Task SaveLastDailyStarringRunAsync(DateTimeOffset runAt, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var connection = CreateConnection();
        await connection.OpenAsync(ct);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO metadata (key, value)
            VALUES ($key, $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """;
        command.Parameters.AddWithValue("$key", LastDailyStarringRunKey);
        command.Parameters.AddWithValue("$value", runAt.ToString("O"));
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteScoresOlderThanAsync(DateOnly keepFromInclusive, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var connection = CreateConnection();
        await connection.OpenAsync(ct);
        await EnableForeignKeysAsync(connection, ct);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            DELETE FROM article_scores
            WHERE score_date < $keepFromDate;
            """;
        command.Parameters.AddWithValue("$keepFromDate", keepFromInclusive.ToString("yyyy-MM-dd"));
        var deleted = await command.ExecuteNonQueryAsync(ct);

        if (deleted > 0)
        {
            _logger.LogInformation("Deleted {Count} stale daily article score record(s)", deleted);
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized)
        {
            return;
        }

        await _initializationLock.WaitAsync(ct);
        try
        {
            if (_initialized)
            {
                return;
            }

            var directory = Path.GetDirectoryName(_databasePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var connection = CreateConnection();
            await connection.OpenAsync(ct);
            await EnableForeignKeysAsync(connection, ct);

            var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE IF NOT EXISTS article_scores (
                    score_date TEXT NOT NULL,
                    entry_id INTEGER NOT NULL,
                    title TEXT NOT NULL,
                    url TEXT NOT NULL,
                    total_score INTEGER NOT NULL,
                    created_at_utc TEXT NOT NULL,
                    updated_at_utc TEXT NOT NULL,
                    PRIMARY KEY (score_date, entry_id)
                );

                CREATE TABLE IF NOT EXISTS article_topic_scores (
                    score_date TEXT NOT NULL,
                    entry_id INTEGER NOT NULL,
                    topic TEXT NOT NULL,
                    score INTEGER NOT NULL,
                    PRIMARY KEY (score_date, entry_id, topic),
                    FOREIGN KEY (score_date, entry_id) REFERENCES article_scores(score_date, entry_id) ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS metadata (
                    key TEXT NOT NULL PRIMARY KEY,
                    value TEXT NOT NULL
                );

                CREATE INDEX IF NOT EXISTS ix_article_scores_daily_ranking
                    ON article_scores (score_date, total_score DESC, entry_id ASC);
                """;
            await command.ExecuteNonQueryAsync(ct);

            _initialized = true;
            _logger.LogInformation("Initialized article score database at {Path}", _databasePath);
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    private SqliteConnection CreateConnection() =>
        new(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString());

    private static async Task EnableForeignKeysAsync(SqliteConnection connection, CancellationToken ct)
    {
        var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        await pragma.ExecuteNonQueryAsync(ct);
    }

    private static async Task<IReadOnlyDictionary<string, int>> LoadTopicScoresAsync(
        SqliteConnection connection,
        DateOnly scoreDate,
        long entryId,
        CancellationToken ct)
    {
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT topic, score
            FROM article_topic_scores
            WHERE score_date = $scoreDate AND entry_id = $entryId
            ORDER BY topic ASC;
            """;
        command.Parameters.AddWithValue("$scoreDate", scoreDate.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$entryId", entryId);

        var topicScores = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            topicScores[reader.GetString(0)] = reader.GetInt32(1);
        }

        return topicScores;
    }
}
