using Microsoft.Extensions.Logging;
using FeedTriage.Worker.Interfaces;

namespace FeedTriage.Worker.Services;

public sealed class DailyArticleStarringService : IDailyArticleStarringService
{
    internal const int MinimumKeepUnreadScore = 6;
    internal const int DailyStarCount = 5;
    internal const int RetentionDays = 5;

    private readonly IArticleScoreRepository _scores;
    private readonly IMinifluxClient _miniflux;
    private readonly ILogger<DailyArticleStarringService> _logger;

    public DailyArticleStarringService(
        IArticleScoreRepository scores,
        IMinifluxClient miniflux,
        ILogger<DailyArticleStarringService> logger)
    {
        _scores = scores;
        _miniflux = miniflux;
        _logger = logger;
    }

    public async Task RunPendingAsync(CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var keepFromDate = today.AddDays(-(RetentionDays - 1));

        try
        {
            var lastRun = await _scores.GetLastDailyStarringRunAsync(ct);
            var lastRunDate = lastRun is null ? (DateOnly?)null : DateOnly.FromDateTime(lastRun.Value.UtcDateTime);
            var firstPendingDate = lastRunDate?.AddDays(1) ?? keepFromDate;
            var lastCompletedDate = today.AddDays(-1);

            if (lastCompletedDate >= firstPendingDate)
            {
                var pendingDates = await _scores.GetScoredDatesAsync(firstPendingDate, lastCompletedDate, ct);

                foreach (var scoreDate in pendingDates)
                {
                    ct.ThrowIfCancellationRequested();

                    var topScores = await _scores.GetTopScoresAsync(
                        scoreDate,
                        DailyStarCount,
                        MinimumKeepUnreadScore,
                        ct);

                    foreach (var article in topScores)
                    {
                        await _miniflux.BookmarkAsync(article.EntryId, ct);
                    }

                    await _scores.SaveLastDailyStarringRunAsync(
                        new DateTimeOffset(scoreDate.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero),
                        ct);

                    _logger.LogInformation(
                        "Daily starring completed for {ScoreDate}: starred {Count} article(s)",
                        scoreDate,
                        topScores.Count);
                }
            }
        }
        finally
        {
            await _scores.DeleteScoresOlderThanAsync(keepFromDate, ct);
        }
    }
}
