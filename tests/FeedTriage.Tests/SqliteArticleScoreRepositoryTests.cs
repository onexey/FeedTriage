using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using FeedTriage.Worker.Configuration;
using FeedTriage.Worker.Models;
using FeedTriage.Worker.Services;
using Xunit;

namespace FeedTriage.Tests;

public sealed class SqliteArticleScoreRepositoryTests
{
    [Fact]
    public async Task SaveScoreAsync_PersistsScoresAndMetadata()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"feedtriage-scores-{Guid.NewGuid():N}");
        var databasePath = Path.Combine(tempRoot, "scores.db");
        var repository = CreateRepository(databasePath);
        var scoreDate = new DateOnly(2026, 5, 14);

        try
        {
            await repository.SaveScoreAsync(new StoredArticleScore
            {
                ScoreDate = scoreDate,
                EntryId = 42,
                Title = "Interesting article",
                Url = "https://example.com/article",
                TotalScore = 7,
                TopicScores = new Dictionary<string, int>
                {
                    ["software engineering"] = 4,
                    ["team leadership"] = 3
                }
            });
            await repository.SaveLastDailyStarringRunAsync(new DateTimeOffset(2026, 5, 15, 0, 5, 0, TimeSpan.Zero));

            var storedScores = await repository.GetTopScoresAsync(scoreDate, 5, 6);
            var storedScore = Assert.Single(storedScores);

            Assert.Equal(42, storedScore.EntryId);
            Assert.Equal(7, storedScore.TotalScore);
            Assert.Equal(4, storedScore.TopicScores["software engineering"]);
            Assert.Equal(3, storedScore.TopicScores["team leadership"]);
            Assert.Equal(new DateTimeOffset(2026, 5, 15, 0, 5, 0, TimeSpan.Zero), await repository.GetLastDailyStarringRunAsync());
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task DeleteScoresOlderThanAsync_RemovesExpiredRowsOnly()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"feedtriage-scores-{Guid.NewGuid():N}");
        var databasePath = Path.Combine(tempRoot, "scores.db");
        var repository = CreateRepository(databasePath);

        try
        {
            await repository.SaveScoreAsync(new StoredArticleScore
            {
                ScoreDate = new DateOnly(2026, 5, 10),
                EntryId = 1,
                Title = "Old",
                Url = "https://example.com/old",
                TotalScore = 7,
                TopicScores = new Dictionary<string, int> { ["software engineering"] = 7 }
            });
            await repository.SaveScoreAsync(new StoredArticleScore
            {
                ScoreDate = new DateOnly(2026, 5, 14),
                EntryId = 2,
                Title = "Recent",
                Url = "https://example.com/recent",
                TotalScore = 6,
                TopicScores = new Dictionary<string, int> { ["software engineering"] = 5 }
            });

            await repository.DeleteScoresOlderThanAsync(new DateOnly(2026, 5, 11));

            Assert.Empty(await repository.GetTopScoresAsync(new DateOnly(2026, 5, 10), 5, 0));
            Assert.Single(await repository.GetTopScoresAsync(new DateOnly(2026, 5, 14), 5, 0));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static SqliteArticleScoreRepository CreateRepository(string databasePath)
    {
        var options = Options.Create(new StateOptions { ScoreDatabasePath = databasePath });
        return new SqliteArticleScoreRepository(options, NullLogger<SqliteArticleScoreRepository>.Instance);
    }
}
