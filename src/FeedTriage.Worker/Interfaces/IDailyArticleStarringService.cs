namespace FeedTriage.Worker.Interfaces;

public interface IDailyArticleStarringService
{
    Task RunPendingAsync(CancellationToken ct = default);
}
