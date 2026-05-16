using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FeedTriage.Worker.Configuration;
using FeedTriage.Worker.Interfaces;
using FeedTriage.Worker.Models;
using FeedTriage.Worker.Utilities;

namespace FeedTriage.Worker.Services;

/// <inheritdoc />
public sealed class ArticleProcessor : IArticleProcessor
{
    private const int KeepUnreadTopicScoreThreshold = 2;

    private readonly IMinifluxClient _miniflux;
    private readonly IAiDecisionPipeline _ai;
    private readonly IRunStateRepository _state;
    private readonly IArticleScoreRepository _scores;
    private readonly FilteringOptions _filtering;
    private readonly ProcessingOptions _processing;
    private readonly ILogger<ArticleProcessor> _logger;
    private readonly IReadOnlyList<IEntryScreeningContentHandler> _screeningContentHandlers;

    public ArticleProcessor(
        IMinifluxClient miniflux,
        IAiDecisionPipeline ai,
        IRunStateRepository state,
        IArticleScoreRepository scores,
        IEnumerable<IEntryScreeningContentHandler> screeningContentHandlers,
        IOptions<FilteringOptions> filteringOptions,
        IOptions<ProcessingOptions> processingOptions,
        ILogger<ArticleProcessor> logger)
    {
        _miniflux = miniflux;
        _ai = ai;
        _state = state;
        _scores = scores;
        _screeningContentHandlers = screeningContentHandlers.ToList();
        _filtering = filteringOptions.Value;
        _processing = processingOptions.Value;
        _logger = logger;
    }

    public async Task<RunSummary> ProcessAsync(CancellationToken ct = default)
    {
        var summary = new RunSummary { StartedAt = DateTimeOffset.UtcNow };

        if (_processing.DryRun)
            _logger.LogInformation("DRY RUN enabled — no Miniflux read-state or local state writes will be performed");

        var afterPublishedAt = await _state.GetLastPublishedAtAsync(ct);

        IReadOnlyList<MinifluxEntry> entries;
        try
        {
            entries = await _miniflux.GetUnreadEntriesAsync(_processing.MaxArticlesPerRun, afterPublishedAt, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to fetch unread entries from Miniflux — aborting run");
            summary.CompletedAt = DateTimeOffset.UtcNow;
            return summary;
        }

        summary.TotalFetched = entries.Count;

        var maxAttempts = _processing.MaxRetriesPerEntry + 1;

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();

            ArticleProcessingResult result = null!;

            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                result = await ProcessEntryAsync(entry, ct);
                if (result.ErrorMessage is null) break;

                if (attempt < maxAttempts)
                {
                    _logger.LogWarning(
                        "Entry {Id} ({Title}): attempt {Attempt}/{MaxAttempts} failed ({Error}) — retrying",
                        entry.Id, entry.Title, attempt, maxAttempts, result.ErrorMessage);
                }
            }

            // All retries exhausted — leave unread so the next run can retry.
            if (result.ErrorMessage is not null && !result.MarkedAsRead)
            {
                _logger.LogWarning(
                    "Entry {Id} ({Title}) {Url}: all {MaxAttempts} attempt(s) failed ({Error}) — leaving unread for next run",
                    entry.Id, entry.Title, entry.Url, maxAttempts, result.ErrorMessage);
            }

            summary.Results.Add(result);

            if (result.ScreeningPassed == true) summary.ScreeningPassed++;
            if (result.ReviewPassed == true) summary.ReviewPassed++;
            summary.RelevantMatches += result.RelevantUrls.Count;
            if (result.MarkedAsRead) summary.MarkedAsRead++;
            if (result.ErrorMessage is not null) summary.Errors++;
            if (result.TopicScores.Count > 0 || result.TotalScore > 0) summary.ScoredEntries++;
        }

        if (entries.Count > 0 && !_processing.DryRun)
        {
            var newestPublishedAt = entries.Max(e => e.PublishedAt);
            await _state.SaveLastPublishedAtAsync(newestPublishedAt, ct);
        }

        summary.CompletedAt = DateTimeOffset.UtcNow;
        _logger.LogInformation(
            "Run complete — fetched:{Fetched} screened:{Screened} reviewed:{Reviewed} " +
            "relevant:{Relevant} scored:{Scored} marked:{Marked} errors:{Errors} elapsed:{Elapsed:g}",
            summary.TotalFetched, summary.ScreeningPassed, summary.ReviewPassed,
            summary.RelevantMatches, summary.ScoredEntries, summary.MarkedAsRead, summary.Errors,
            summary.CompletedAt - summary.StartedAt);

        return summary;
    }

    private async Task<ArticleProcessingResult> ProcessEntryAsync(
        MinifluxEntry entry, CancellationToken ct)
    {
        var result = new ArticleProcessingResult
        {
            EntryId = entry.Id,
            Url = entry.Url,
            Title = entry.Title
        };

        try
        {
            var candidatesResult = await BuildScreeningCandidatesAsync(entry, ct);
            if (candidatesResult.ErrorMessage is not null)
            {
                _logger.LogWarning(
                    "Entry {Id} ({Title}): {ErrorMessage} — leaving unread",
                    entry.Id,
                    entry.Title,
                    candidatesResult.ErrorMessage);
                result.ErrorMessage = candidatesResult.ErrorMessage;
                return result;
            }

            var matchingCandidates = new List<(ScreeningCandidate Candidate, AiDecision ReviewDecision)>();
            (ScreeningCandidate Candidate, AiDecision ReviewDecision)? bestScoreResult = null;

            foreach (var candidate in candidatesResult.Candidates)
            {
                var screenDecision = await _ai.EvaluateScreeningAsync(candidate.Title, candidate.ScreeningText, ct);

                if (screenDecision is null)
                {
                    _logger.LogWarning(
                        "Entry {Id} {CandidateType}: all screening providers failed — leaving unread",
                        entry.Id, candidate.CandidateType);
                    result.ErrorMessage = $"All screening providers failed for {candidate.CandidateType}";
                    return result;
                }

                result.ScreeningPassed = (result.ScreeningPassed ?? false) || screenDecision.Passed;
                result.DecisionReason = screenDecision.Reason;

                _logger.LogInformation(
                    "Entry {Id} {CandidateType} screening: passed={Passed} [{Provider}/{Model}] reason={Reason}",
                    entry.Id,
                    candidate.CandidateType,
                    screenDecision.Passed,
                    screenDecision.ProviderInstance,
                    screenDecision.Model,
                    screenDecision.Reason);

                if (!screenDecision.Passed)
                {
                    continue;
                }

                var fullHtml = candidate.PrefetchedFullHtml;
                if (fullHtml is null)
                {
                    fullHtml = await _miniflux.FetchContentAsync(entry.Id, ct);
                }

                if (fullHtml is null)
                {
                    _logger.LogWarning(
                        "Entry {Id} {CandidateType}: full-content fetch failed — leaving unread",
                        entry.Id, candidate.CandidateType);
                    result.ErrorMessage = $"Full-content fetch failed for {candidate.CandidateType}";
                    return result;
                }

                var fullText = HtmlTextExtractor.Extract(fullHtml);
                var reviewDecision = await _ai.EvaluateReviewAsync(candidate.Title, fullText, ct);

                if (reviewDecision is null)
                {
                    _logger.LogWarning(
                        "Entry {Id} {CandidateType}: all review providers failed — leaving unread",
                        entry.Id, candidate.CandidateType);
                    result.ErrorMessage = $"All review providers failed for {candidate.CandidateType}";
                    return result;
                }

                if (reviewDecision.TopicScores.Count == 0)
                {
                    _logger.LogWarning(
                        "Entry {Id} {CandidateType}: review response did not include topic scores — leaving unread",
                        entry.Id, candidate.CandidateType);
                    result.ErrorMessage = $"Topic scores missing for {candidate.CandidateType}";
                    return result;
                }

                var keepUnread = HasRelevantTopicScore(reviewDecision.TopicScores);
                result.ReviewPassed = (result.ReviewPassed ?? false) || keepUnread;
                result.DecisionReason = reviewDecision.Reason;

                if (bestScoreResult is null || reviewDecision.TotalScore > bestScoreResult.Value.ReviewDecision.TotalScore)
                {
                    bestScoreResult = (candidate, reviewDecision);
                }

                _logger.LogInformation(
                        "Entry {Id} {CandidateType} review: passed={Passed} totalScore={TotalScore} [{Provider}/{Model}] reason={Reason}",
                        entry.Id,
                        candidate.CandidateType,
                        keepUnread,
                        reviewDecision.TotalScore,
                        reviewDecision.ProviderInstance,
                        reviewDecision.Model,
                        reviewDecision.Reason);

                if (keepUnread)
                {
                    matchingCandidates.Add((candidate, reviewDecision));
                }
            }

            if (bestScoreResult is not null)
            {
                ApplyTopicScores(result, bestScoreResult.Value.ReviewDecision.TopicScores);
                LogRatingResult(entry, bestScoreResult.Value.Candidate, bestScoreResult.Value.ReviewDecision, result);
                await TryPersistScoreAsync(entry, result, ct);
            }

            if (matchingCandidates.Count == 0)
            {
                await MarkReadAsync(entry.Id, result, ct);
                return result;
            }

            foreach (var (candidate, reviewDecision) in matchingCandidates)
            {
                result.RelevantUrls.Add(candidate.Url);

                if (_processing.DryRun)
                {
                    _logger.LogInformation(
                        "[DRY RUN] Relevant candidate would remain unread: {Url} ({CandidateType}) — reason: {Reason}",
                        candidate.Url,
                        candidate.CandidateType,
                        reviewDecision.Reason);
                }
            }

            if (!_processing.DryRun)
            {
                _logger.LogInformation(
                    "Entry {Id} ({Title}): {Count} relevant candidate(s) found — leaving unread for manual review",
                    entry.Id,
                    entry.Title,
                    result.RelevantUrls.Count);
            }

            if (_processing.DryRun)
            {
                _logger.LogInformation(
                    "[DRY RUN] Entry {Id} ({Title}) would remain unread because it has relevant candidates",
                    entry.Id, entry.Title);
            }
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error processing entry {Id}", entry.Id);
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    private async Task MarkReadAsync(long entryId, ArticleProcessingResult result, CancellationToken ct)
    {
        if (_processing.DryRun) return;

        try
        {
            await _miniflux.MarkAsReadAsync([entryId], ct);
            result.MarkedAsRead = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Accepted risk: article may be re-processed on next run.
            _logger.LogWarning(ex,
                "Entry {Id}: mark-as-read failed — article may be re-processed next run", entryId);
        }
    }

    private void ApplyTopicScores(
        ArticleProcessingResult result,
        IReadOnlyDictionary<string, int> rawTopicScores)
    {
        result.TopicScores.Clear();

        foreach (var (topic, score) in NormalizeTopicScores(rawTopicScores))
        {
            result.TopicScores[topic] = score;
        }

        result.TotalScore = result.TopicScores.Values.Sum();
        result.ReviewPassed = HasRelevantTopicScore(result.TopicScores);
    }

    private void LogRatingResult(
        MinifluxEntry entry,
        ScreeningCandidate candidate,
        AiDecision reviewDecision,
        ArticleProcessingResult result)
    {
        _logger.LogInformation(
            "Entry {Id} ({Title}) rating result: candidate={CandidateType} totalScore={TotalScore} topicScores={TopicScores} [{Provider}/{Model}] reason={Reason}",
            entry.Id,
            entry.Title,
            candidate.CandidateType,
            result.TotalScore,
            FormatTopicScores(result.TopicScores),
            reviewDecision.ProviderInstance,
            reviewDecision.Model,
            reviewDecision.Reason);
    }

    private IReadOnlyDictionary<string, int> NormalizeTopicScores(
        IReadOnlyDictionary<string, int> rawTopicScores)
    {
        var normalized = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var lookup = new Dictionary<string, int>(rawTopicScores, StringComparer.OrdinalIgnoreCase);

        foreach (var topic in _filtering.GetFocusTopicList())
        {
            lookup.TryGetValue(topic, out var score);
            normalized[topic] = Math.Clamp(score, 0, 5);
        }

        return normalized;
    }

    private static string FormatTopicScores(IReadOnlyDictionary<string, int> topicScores) =>
        string.Join(", ", topicScores.Select(kvp => $"{kvp.Key}={kvp.Value}"));

    private async Task TryPersistScoreAsync(
        MinifluxEntry entry,
        ArticleProcessingResult result,
        CancellationToken ct)
    {
        if (_processing.DryRun || result.TopicScores.Count == 0)
        {
            return;
        }

        try
        {
            await _scores.SaveScoreAsync(
                new StoredArticleScore
                {
                    ScoreDate = DateOnly.FromDateTime(DateTime.UtcNow),
                    EntryId = entry.Id,
                    Title = entry.Title,
                    Url = entry.Url,
                    TotalScore = result.TotalScore,
                    TopicScores = new Dictionary<string, int>(result.TopicScores, StringComparer.OrdinalIgnoreCase)
                },
                ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist topic scores for entry {EntryId}", entry.Id);
        }
    }

    private static bool HasRelevantTopicScore(IReadOnlyDictionary<string, int> topicScores) =>
        topicScores.Values.Any(score => score > KeepUnreadTopicScoreThreshold);

    private async Task<ScreeningContentResult> BuildScreeningCandidatesAsync(
        MinifluxEntry entry,
        CancellationToken ct)
    {
        var feedExcerpt = HtmlTextExtractor.ExtractExcerpt(entry.Content);

        foreach (var handler in _screeningContentHandlers)
        {
            var result = await handler.TryBuildAsync(entry, ct);
            if (result is not null)
            {
                return result;
            }
        }

        return new ScreeningContentResult
        {
            Candidates =
            [
                new ScreeningCandidate
                {
                    CandidateType = "article",
                    Title = entry.Title,
                    Url = entry.Url,
                    ScreeningText = feedExcerpt
                }
            ]
        };
    }
}
