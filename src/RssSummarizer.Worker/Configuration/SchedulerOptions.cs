using System.ComponentModel.DataAnnotations;

namespace RssSummarizer.Worker.Configuration;

public sealed class SchedulerOptions
{
    public const string SectionName = "RssSummarizer:Scheduler";

    public bool RunOnStart { get; set; } = true;

    [Required]
    public TimeSpan RunInterval { get; set; } = TimeSpan.FromDays(1);
}
