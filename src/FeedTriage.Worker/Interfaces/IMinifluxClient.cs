using FeedTriage.Worker.Models;

namespace FeedTriage.Worker.Interfaces;

public interface IMinifluxClient
{
    /// <summary>
    /// Returns unread entries from Miniflux published after <paramref name="after"/>,
    /// ordered by published date ascending (oldest first).
    /// Pass null to fetch all unread entries (first run).
    /// </summary>
    Task<IReadOnlyList<MinifluxEntry>> GetUnreadEntriesAsync(
        int? limit = null,
        DateTimeOffset? after = null,
        CancellationToken ct = default);

    /// <summary>
    /// Fetches the full original article content via Miniflux's scraper endpoint.
    /// Returns null if the fetch fails (caller must treat this as a pipeline failure and leave the entry unread).
    /// </summary>
    Task<string?> FetchContentAsync(long entryId, CancellationToken ct = default);

    /// <summary>Marks the given entries as read.</summary>
    Task MarkAsReadAsync(IEnumerable<long> entryIds, CancellationToken ct = default);
}
