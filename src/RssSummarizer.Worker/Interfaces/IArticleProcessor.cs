using RssSummarizer.Worker.Models;

namespace RssSummarizer.Worker.Interfaces;

/// <summary>
/// Fetches unread Miniflux entries and runs the full two-stage AI filtering pipeline.
/// </summary>
public interface IArticleProcessor
{
    Task<RunSummary> ProcessAsync(CancellationToken ct = default);
}
