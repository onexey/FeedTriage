namespace FeedTriage.Worker.Models;

public sealed class RunSummary
{
    public required DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset CompletedAt { get; set; }

    public int TotalFetched { get; set; }
    public int ScreeningPassed { get; set; }
    public int ReviewPassed { get; set; }
    public int RelevantMatches { get; set; }
    public int MarkedAsRead { get; set; }
    public int Errors { get; set; }

    public List<ArticleProcessingResult> Results { get; init; } = [];
}
