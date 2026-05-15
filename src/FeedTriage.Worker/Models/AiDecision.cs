namespace FeedTriage.Worker.Models;

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

    /// <summary>
    /// Per-topic scores from 0-5 for the evaluated article. Empty for screening-only decisions.
    /// </summary>
    public IReadOnlyDictionary<string, int> TopicScores { get; init; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Sum of <see cref="TopicScores"/> for convenience when ranking entries.
    /// </summary>
    public int TotalScore => TopicScores.Values.Sum();
}
