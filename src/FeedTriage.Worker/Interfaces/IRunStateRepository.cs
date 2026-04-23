namespace FeedTriage.Worker.Interfaces;

public interface IRunStateRepository
{
    /// <summary>
    /// Returns the publication date of the newest entry seen in the last completed run,
    /// or null if no run has completed yet (first run ever).
    /// </summary>
    Task<DateTimeOffset?> GetLastPublishedAtAsync(CancellationToken ct = default);

    /// <summary>
    /// Persists the publication date of the newest entry seen in the current run so
    /// the next run can fetch only entries published after this point.
    /// </summary>
    Task SaveLastPublishedAtAsync(DateTimeOffset publishedAt, CancellationToken ct = default);
}
