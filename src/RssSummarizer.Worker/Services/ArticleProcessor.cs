using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RssSummarizer.Worker.Configuration;
using RssSummarizer.Worker.Interfaces;
using RssSummarizer.Worker.Models;
using RssSummarizer.Worker.Utilities;

namespace RssSummarizer.Worker.Services;

/// <inheritdoc />
public sealed class ArticleProcessor : IArticleProcessor
{
    private readonly IMinifluxClient _miniflux;
    private readonly IAiDecisionPipeline _ai;
    private readonly IRunStateRepository _state;
    private readonly ProcessingOptions _processing;
    private readonly ILogger<ArticleProcessor> _logger;
    private readonly IReadOnlyList<IEntryScreeningContentHandler> _screeningContentHandlers;

    public ArticleProcessor(
        IMinifluxClient miniflux,
        IAiDecisionPipeline ai,
        IRunStateRepository state,
        IEnumerable<IEntryScreeningContentHandler> screeningContentHandlers,
        IOptions<ProcessingOptions> processingOptions,
        ILogger<ArticleProcessor> logger)
    {
        _miniflux = miniflux;
        _ai = ai;
        _state = state;
        _screeningContentHandlers = screeningContentHandlers.ToList();
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
        }

        if (entries.Count > 0 && !_processing.DryRun)
        {
            var newestPublishedAt = entries.Max(e => e.PublishedAt);
            await _state.SaveLastPublishedAtAsync(newestPublishedAt, ct);
        }

        summary.CompletedAt = DateTimeOffset.UtcNow;
        _logger.LogInformation(
            "Run complete — fetched:{Fetched} screened:{Screened} reviewed:{Reviewed} " +
            "relevant:{Relevant} marked:{Marked} errors:{Errors} elapsed:{Elapsed:g}",
            summary.TotalFetched, summary.ScreeningPassed, summary.ReviewPassed,
            summary.RelevantMatches, summary.MarkedAsRead, summary.Errors,
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

                result.ReviewPassed = (result.ReviewPassed ?? false) || reviewDecision.Passed;
                result.DecisionReason = reviewDecision.Reason;

                _logger.LogInformation(
                    "Entry {Id} {CandidateType} review: passed={Passed} [{Provider}/{Model}] reason={Reason}",
                    entry.Id,
                    candidate.CandidateType,
                    reviewDecision.Passed,
                    reviewDecision.ProviderInstance,
                    reviewDecision.Model,
                    reviewDecision.Reason);

                if (reviewDecision.Passed)
                {
                    matchingCandidates.Add((candidate, reviewDecision));
                }
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
