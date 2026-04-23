namespace RssSummarizer.Worker.Models;

public sealed class ScreeningContentResult
{
    public required IReadOnlyList<ScreeningCandidate> Candidates { get; init; }

    public string? ErrorMessage { get; init; }
}
