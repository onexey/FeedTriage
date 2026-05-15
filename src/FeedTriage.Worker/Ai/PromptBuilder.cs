using FeedTriage.Worker.Configuration;

namespace FeedTriage.Worker.Ai;

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
        var focusTopics = filtering.GetFocusTopicList();
        var focusTopicsText = string.Join(", ", focusTopics);
        var scoringJsonShape = string.Join(", ", focusTopics.Select(topic => $"\"{topic}\": 0"));
        var antiTopicsSection = filtering.GetAntiTopicList() is { Count: > 0 } antiTopics
            ? $"\nTopics to AVOID: {string.Join(", ", antiTopics)}"
            : string.Empty;

        // Truncate content so we don't blow out the context window
        var content = fullContent.Length > MaxContentCharsForReview
            ? fullContent[..MaxContentCharsForReview] + "…[truncated]"
            : fullContent;

        return $$"""
            You are a relevance reviewer. Your task is to score how valuable an article is for each target topic.

            Relevant topics: {{focusTopicsText}}{{antiTopicsSection}}

            Score every target topic from 0 to 5 using ONLY integers:
            - 0 = unrelated to that topic
            - 1 = barely related and not useful
            - 2 = somewhat related but low value
            - 3 = clearly related and somewhat useful
            - 4 = strongly related and valuable
            - 5 = extremely related and highly valuable

            Value matters as much as topicality. Do not give high scores just because a topic is mentioned in passing.

            Article title: {{title}}
            Article content:
            {{content}}

            Respond with ONLY a JSON object in this exact format (no extra text, no markdown):
            {"passed": true, "reason": "one short sentence explaining your decision", "topicScores": { {{scoringJsonShape}} }}

            Use the exact target topics above as the keys inside "topicScores".
            Set "passed" to true only if the sum of all topic scores is 6 or higher. Otherwise set it to false.
            """;
    }
}
