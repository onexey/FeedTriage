namespace RssSummarizer.Worker.Models;

/// <summary>
/// Normalised result from a single AI evaluation stage.
/// Both screening and full-review return this same contract.
/// </summary>
public sealed class AiDecision
{
    /// <summary>
    /// True when the article passed this stage (either "worth a full review" or "worth keeping unread for follow-up").
    /// </summary>
    public required bool Passed { get; init; }

    /// <summary>
    /// A short human-readable explanation from the model. Used for logging and dry-run output only.
    /// </summary>
    public required string Reason { get; init; }

    /// <summary>
    /// The named provider instance that produced this decision (e.g. "screen_ollama_small").
    /// </summary>
    public required string ProviderInstance { get; init; }

    /// <summary>
    /// The model identifier used by the provider (e.g. "qwen3:4b").
    /// </summary>
    public required string Model { get; init; }
}
