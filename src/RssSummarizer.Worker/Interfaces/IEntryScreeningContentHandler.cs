using RssSummarizer.Worker.Models;

namespace RssSummarizer.Worker.Interfaces;

public interface IEntryScreeningContentHandler
{
    Task<ScreeningContentResult?> TryBuildAsync(MinifluxEntry entry, CancellationToken ct = default);
}