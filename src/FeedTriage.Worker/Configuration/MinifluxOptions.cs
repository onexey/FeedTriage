using System.ComponentModel.DataAnnotations;

namespace FeedTriage.Worker.Configuration;

public sealed class MinifluxOptions
{
    public const string SectionName = "FeedTriage:Miniflux";

    [Required, Url]
    public string BaseUrl { get; set; } = string.Empty;

    [Required]
    public string ApiToken { get; set; } = string.Empty;
}
