using System.ComponentModel.DataAnnotations;

namespace FeedTriage.Worker.Configuration;

public sealed class SchedulerOptions
{
    public const string SectionName = "FeedTriage:Scheduler";

    public bool RunOnStart { get; set; } = true;

    [Required]
    public TimeSpan RunInterval { get; set; } = TimeSpan.FromDays(1);
}
