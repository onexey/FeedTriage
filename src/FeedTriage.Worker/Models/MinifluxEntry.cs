using System.Text.Json.Serialization;

namespace FeedTriage.Worker.Models;

public sealed class MinifluxEntry
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("comments_url")]
    public string? CommentsUrl { get; set; }

    [JsonPropertyName("author")]
    public string? Author { get; set; }

    /// <summary>
    /// HTML content from the RSS feed. May be empty.
    /// </summary>
    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("published_at")]
    public DateTimeOffset PublishedAt { get; set; }
}

/// <summary>Response envelope returned by GET /v1/entries.</summary>
public sealed class MinifluxEntriesResponse
{
    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("entries")]
    public List<MinifluxEntry> Entries { get; set; } = [];
}

/// <summary>Response envelope returned by GET /v1/entries/{id}/fetch-content.</summary>
public sealed class MinifluxFetchContentResponse
{
    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;
}
