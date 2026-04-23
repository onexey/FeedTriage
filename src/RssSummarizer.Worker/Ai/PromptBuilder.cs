using RssSummarizer.Worker.Configuration;

namespace RssSummarizer.Worker.Ai;

/// <summary>
/// Builds the prompts sent to AI providers for each evaluation stage.
/// Prompts are kept separate from provider implementations so they can be
/// modified or extended without touching provider logic.
/// </summary>
public static class PromptBuilder
{
    private const int MaxContentCharsForReview = 8000;

    /// <summary>
    /// Stage 1 prompt: given a title and short excerpt, decide if the article
    /// warrants a full review.
    /// </summary>
    public static string BuildScreeningPrompt(string title, string excerpt, FilteringOptions filtering)
    {
        var focusTopics = string.Join(", ", filtering.GetFocusTopicList());
        var antiTopicsSection = filtering.GetAntiTopicList() is { Count: > 0 } antiTopics
            ? $"\nTopics to AVOID (reduce false positives): {string.Join(", ", antiTopics)}"
            : string.Empty;

        return $$"""
            You are a relevance screener. Your task is to decide whether an article is worth reading in full.

            Relevant topics: {{focusTopics}}{{antiTopicsSection}}

            Article title: {{title}}
            Article excerpt:
            {{excerpt}}

            Respond with ONLY a JSON object in this exact format (no extra text, no markdown):
            {"passed": true, "reason": "one short sentence explaining your decision"}

            Set "passed" to true if the article is likely relevant, false otherwise.
            """;
    }

    /// <summary>
    /// Stage 2 prompt: given a title and full article content, decide if the article
    /// is relevant enough to keep unread for manual review.
    /// </summary>
    public static string BuildReviewPrompt(string title, string fullContent, FilteringOptions filtering)
    {
        var focusTopics = string.Join(", ", filtering.GetFocusTopicList());
        var antiTopicsSection = filtering.GetAntiTopicList() is { Count: > 0 } antiTopics
            ? $"\nTopics to AVOID: {string.Join(", ", antiTopics)}"
            : string.Empty;

        // Truncate content so we don't blow out the context window
        var content = fullContent.Length > MaxContentCharsForReview
            ? fullContent[..MaxContentCharsForReview] + "…[truncated]"
            : fullContent;

        return $$"""
            You are a relevance reviewer. Your task is to decide whether an article is worth keeping unread for careful manual review.

            Relevant topics: {{focusTopics}}{{antiTopicsSection}}

            Article title: {{title}}
            Article content:
            {{content}}

            Respond with ONLY a JSON object in this exact format (no extra text, no markdown):
            {"passed": true, "reason": "one short sentence explaining your decision"}

            Set "passed" to true if the article is genuinely relevant and worth keeping unread for follow-up, false otherwise.
            """;
    }
}
