namespace FeedTriage.Worker.Models;

public sealed class ScreeningCandidate
{
    public required string CandidateType { get; init; }

    public required string Title { get; init; }

    public required string Url { get; init; }

    public required string ScreeningText { get; init; }

    public string? PrefetchedFullHtml { get; init; }
}
