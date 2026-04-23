namespace RssSummarizer.Worker.Models;

public sealed class ArticleProcessingResult
{
    public required long EntryId { get; init; }
    public required string Url { get; init; }
    public required string Title { get; init; }

    /// <summary>Null when Stage 1 was never attempted because processing aborted before evaluation.</summary>
    public bool? ScreeningPassed { get; set; }

    /// <summary>Null when Stage 2 was not reached.</summary>
    public bool? ReviewPassed { get; set; }

    public bool MarkedAsRead { get; set; }

    /// <summary>Human-readable reason from the last evaluated stage. Null if no AI evaluation occurred.</summary>
    public string? DecisionReason { get; set; }

    /// <summary>Error message if any step failed unexpectedly.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Every candidate URL that passed full review for this Miniflux entry.</summary>
    public List<string> RelevantUrls { get; } = [];
}
