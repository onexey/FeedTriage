using Microsoft.Extensions.Logging;
using RssSummarizer.Worker.Interfaces;
using RssSummarizer.Worker.Models;
using RssSummarizer.Worker.Utilities;

namespace RssSummarizer.Worker.Services;

public sealed class HackerNewsScreeningContentHandler : IEntryScreeningContentHandler
{
    private const int ArticleExcerptChars = 2500;
    private const int DiscussionExcerptChars = 1500;
    private const string CommentsCandidateType = "comments";
    private const string ArticleCandidateType = "article";

    private readonly IMinifluxClient _miniflux;
    private readonly HttpClient _contentHttp;
    private readonly ILogger<HackerNewsScreeningContentHandler> _logger;

    public HackerNewsScreeningContentHandler(
        IMinifluxClient miniflux,
        IHttpClientFactory httpClientFactory,
        ILogger<HackerNewsScreeningContentHandler> logger)
    {
        _miniflux = miniflux;
        _contentHttp = httpClientFactory.CreateClient();
        _logger = logger;
    }

    public async Task<ScreeningContentResult?> TryBuildAsync(MinifluxEntry entry, CancellationToken ct = default)
    {
        if (!IsHackerNewsCommentsUrl(entry.CommentsUrl))
        {
            return null;
        }

        var candidates = new List<ScreeningCandidate>();
        var feedExcerpt = HtmlTextExtractor.ExtractExcerpt(entry.Content);
        var hasExternalArticle = HasExternalArticleUrl(entry);

        if (hasExternalArticle)
        {
            var articleCandidate = await BuildArticleCandidateAsync(entry, feedExcerpt, ct);
            candidates.Add(articleCandidate);
        }

        var commentsCandidate = await BuildCommentsCandidateAsync(entry, ct);
        if (commentsCandidate is null)
        {
            return new ScreeningContentResult
            {
                Candidates = candidates,
                ErrorMessage = "Failed to prepare the Hacker News discussion for evaluation."
            };
        }

        candidates.Add(commentsCandidate);

        return new ScreeningContentResult { Candidates = candidates };
    }

    private async Task<ScreeningCandidate> BuildArticleCandidateAsync(
        MinifluxEntry entry,
        string feedExcerpt,
        CancellationToken ct)
    {
        var parts = new List<string>();
        if (!IsCommentsOnlyExcerpt(feedExcerpt))
        {
            parts.Add($"Feed excerpt:\n{feedExcerpt}");
        }

        string? articleHtml = null;
        if (NeedsArticleFetch(feedExcerpt))
        {
            articleHtml = await TryFetchEntryContentAsync(entry.Id, ct);
            if (articleHtml is null)
            {
                _logger.LogWarning(
                    "Entry {Id}: article content fetch failed — will screen on title only",
                    entry.Id);
            }
            else
            {
                var articleExcerpt = HtmlTextExtractor.ExtractExcerpt(articleHtml, ArticleExcerptChars);
                if (!string.IsNullOrWhiteSpace(articleExcerpt))
                {
                    parts.Add($"Article excerpt:\n{articleExcerpt}");
                }
            }
        }

        var screeningText = parts.Count > 0 ? string.Join("\n\n", parts) : feedExcerpt;
        if (string.IsNullOrWhiteSpace(screeningText))
        {
            screeningText = entry.Title;
        }

        return new ScreeningCandidate
        {
            CandidateType = ArticleCandidateType,
            Title = entry.Title,
            Url = entry.Url,
            ScreeningText = screeningText,
            PrefetchedFullHtml = articleHtml
        };
    }

    private async Task<ScreeningCandidate?> BuildCommentsCandidateAsync(
        MinifluxEntry entry,
        CancellationToken ct)
    {
        var discussionHtml = await FetchDiscussionHtmlAsync(entry.CommentsUrl!, ct);
        if (discussionHtml is null)
        {
            return null;
        }

        var discussionExcerpt = HtmlTextExtractor.ExtractExcerpt(discussionHtml, DiscussionExcerptChars);
        if (string.IsNullOrWhiteSpace(discussionExcerpt))
        {
            discussionExcerpt = "No readable Hacker News discussion text was extracted.";
        }

        return new ScreeningCandidate
        {
            CandidateType = CommentsCandidateType,
            Title = $"{entry.Title} (Hacker News discussion)",
            Url = entry.CommentsUrl!,
            ScreeningText = $"Discussion excerpt:\n{discussionExcerpt}",
            PrefetchedFullHtml = discussionHtml
        };
    }

    private async Task<string?> TryFetchEntryContentAsync(long entryId, CancellationToken ct)
    {
        try
        {
            return await _miniflux.FetchContentAsync(entryId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to fetch Miniflux article content for entry {EntryId}", entryId);
            return null;
        }
    }

    private async Task<string?> FetchDiscussionHtmlAsync(string discussionUrl, CancellationToken ct)
    {
        try
        {
            var response = await _contentHttp.GetAsync(discussionUrl, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Discussion fetch for {Url} returned {StatusCode}",
                    discussionUrl, (int)response.StatusCode);
                return null;
            }

            return await response.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to fetch discussion content from {Url}", discussionUrl);
            return null;
        }
    }

    private static bool HasExternalArticleUrl(MinifluxEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Url))
        {
            return false;
        }

        if (IsHackerNewsCommentsUrl(entry.Url))
        {
            return false;
        }

        return !string.Equals(entry.Url, entry.CommentsUrl, StringComparison.OrdinalIgnoreCase);
    }

    private static bool NeedsArticleFetch(string feedExcerpt)
    {
        return string.IsNullOrWhiteSpace(feedExcerpt)
            || string.Equals(feedExcerpt, "Comments", StringComparison.OrdinalIgnoreCase)
            || feedExcerpt.Length < 80;
    }

    private static bool IsCommentsOnlyExcerpt(string feedExcerpt)
    {
        return string.IsNullOrWhiteSpace(feedExcerpt)
            || string.Equals(feedExcerpt, "Comments", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHackerNewsCommentsUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return string.Equals(uri.Host, "news.ycombinator.com", StringComparison.OrdinalIgnoreCase);
    }
}
