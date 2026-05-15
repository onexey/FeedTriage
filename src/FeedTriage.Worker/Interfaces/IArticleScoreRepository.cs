using FeedTriage.Worker.Models;

namespace FeedTriage.Worker.Interfaces;

public interface IArticleScoreRepository
{
    Task SaveScoreAsync(StoredArticleScore score, CancellationToken ct = default);
    Task<IReadOnlyList<DateOnly>> GetScoredDatesAsync(DateOnly fromInclusive, DateOnly toInclusive, CancellationToken ct = default);
    Task<IReadOnlyList<StoredArticleScore>> GetTopScoresAsync(DateOnly scoreDate, int take, int minimumTotalScore, CancellationToken ct = default);
    Task<DateTimeOffset?> GetLastDailyStarringRunAsync(CancellationToken ct = default);
    Task SaveLastDailyStarringRunAsync(DateTimeOffset runAt, CancellationToken ct = default);
    Task DeleteScoresOlderThanAsync(DateOnly keepFromInclusive, CancellationToken ct = default);
}
