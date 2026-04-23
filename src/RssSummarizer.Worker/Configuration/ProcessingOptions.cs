namespace RssSummarizer.Worker.Configuration;

public sealed class ProcessingOptions
{
    public const string SectionName = "RssSummarizer:Processing";

    /// <summary>
    /// Maximum number of unread articles to fetch and process per run. Null means unlimited.
    /// </summary>
    public int? MaxArticlesPerRun { get; set; }

    /// <summary>
    /// When true, fetches and evaluates articles but never marks entries as read.
    /// Logs all decisions and reasons.
    /// </summary>
    public bool DryRun { get; set; } = false;

    /// <summary>
    /// Number of times to retry a failed entry before giving up and marking it as read.
    /// Set to 0 to disable retries (leave failed entries unread for the next run).
    /// </summary>
    public int MaxRetriesPerEntry { get; set; } = 5;
}
