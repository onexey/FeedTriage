using System.ComponentModel.DataAnnotations;

namespace FeedTriage.Worker.Configuration;

public sealed class AiOptions
{
    public const string SectionName = "FeedTriage:AI";

    /// <summary>
    /// Comma-separated ordered list of provider instance names for Stage 1 (screening).
    /// Example: "screen_ollama_small,screen_ollama_fallback"
    /// </summary>
    [Required]
    public string ScreeningChain { get; set; } = "screen_ollama_small";

    /// <summary>
    /// Comma-separated ordered list of provider instance names for Stage 2 (full review).
    /// Example: "review_ollama_large"
    /// </summary>
    [Required]
    public string ReviewChain { get; set; } = "review_ollama_large";

    /// <summary>
    /// Named provider instances. Keys are case-insensitive instance names.
    /// </summary>
    public Dictionary<string, ProviderOptions> Providers { get; set; } =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["screen_ollama_small"] = new()
            {
                Model = "ministral-3:3b"
            },
            ["review_ollama_large"] = new()
            {
                Model = "gemma3:27b",
                TimeoutSeconds = 180
            }
        };

    public IReadOnlyList<string> GetScreeningChain() =>
        ScreeningChain.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public IReadOnlyList<string> GetReviewChain() =>
        ReviewChain.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

public sealed class ProviderOptions
{
    /// <summary>
    /// Provider type. Currently only "ollama" is supported.
    /// </summary>
    [Required]
    public string Type { get; set; } = "ollama";

    /// <summary>
    /// Base URL of the Ollama Cloud API. Example: https://api.ollama.com
    /// </summary>
    [Required, Url]
    public string BaseUrl { get; set; } = "https://ollama.com/api";

    [Required]
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// Ollama Cloud API key. Used as a Bearer token in the Authorization header.
    /// </summary>
    [Required]
    public string ApiKey { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 60;
}
