using FeedTriage.Worker.Models;

namespace FeedTriage.Worker.Interfaces;

public interface IEntryScreeningContentHandler
{
    Task<ScreeningContentResult?> TryBuildAsync(MinifluxEntry entry, CancellationToken ct = default);
}