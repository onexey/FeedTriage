using System.ComponentModel.DataAnnotations;

namespace RssSummarizer.Worker.Configuration;

public sealed class MinifluxOptions
{
    public const string SectionName = "RssSummarizer:Miniflux";

    [Required, Url]
    public string BaseUrl { get; set; } = string.Empty;

    [Required]
    public string ApiToken { get; set; } = string.Empty;
}
