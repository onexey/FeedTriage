using FeedTriage.Worker.Models;

namespace FeedTriage.Worker.Interfaces;

/// <summary>
/// Orchestrates the two-stage AI evaluation pipeline with per-stage provider fallback chains.
/// Returns null when all providers in a stage fail — the caller must leave the article unread.
/// </summary>
public interface IAiDecisionPipeline
{
    /// <summary>
    /// Stage 1: determines whether the article is promising enough for a full review.
    /// Input is the article title and a short excerpt derived from RSS feed content.
    /// </summary>
    Task<AiDecision?> EvaluateScreeningAsync(string title, string excerpt, CancellationToken ct = default);

    /// <summary>
    /// Stage 2: determines whether the article is relevant enough to keep unread for manual review.
    /// Input is the article title and the full original article content.
    /// </summary>
    Task<AiDecision?> EvaluateReviewAsync(string title, string fullContent, CancellationToken ct = default);
}
