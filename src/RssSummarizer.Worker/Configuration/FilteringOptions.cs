using System.ComponentModel.DataAnnotations;

namespace RssSummarizer.Worker.Configuration;

public sealed class FilteringOptions
{
    public const string SectionName = "RssSummarizer:Filtering";

    /// <summary>
    /// Comma-separated list of topics the worker should focus on. Required.
    /// Example: "software engineering,software architecture,team leadership"
    /// </summary>
    [Required]
    public string FocusTopics { get; set; } = string.Empty;

    /// <summary>
    /// Comma-separated list of topics to avoid (reduces false positives). Optional.
    /// </summary>
    public string AntiTopics { get; set; } = string.Empty;

    public IReadOnlyList<string> GetFocusTopicList() =>
        FocusTopics.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public IReadOnlyList<string> GetAntiTopicList() =>
        AntiTopics.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
