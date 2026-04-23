using FeedTriage.Worker.Models;

namespace FeedTriage.Worker.Interfaces;

/// <summary>
/// Abstraction for a single, named AI provider instance.
/// Implementations must be stateless and safe to call concurrently.
/// </summary>
public interface IAiProvider
{
    /// <summary>The named instance identifier (e.g. "screen_ollama_small").</summary>
    string InstanceName { get; }

    /// <summary>The model identifier in use (e.g. "qwen3:4b").</summary>
    string Model { get; }

    /// <summary>
    /// Evaluates the given prompt and returns a normalised decision, or null if the
    /// provider fails, times out, or returns output that cannot be parsed.
    /// Callers must never throw on a null return — they must move to the next provider.
    /// </summary>
    Task<AiDecision?> EvaluateAsync(string prompt, CancellationToken ct = default);
}
