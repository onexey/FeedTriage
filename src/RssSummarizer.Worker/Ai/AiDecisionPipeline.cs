using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RssSummarizer.Worker.Configuration;
using RssSummarizer.Worker.Interfaces;
using RssSummarizer.Worker.Models;

namespace RssSummarizer.Worker.Ai;

/// <inheritdoc />
public sealed class AiDecisionPipeline : IAiDecisionPipeline
{
    private readonly AiOptions _aiOptions;
    private readonly FilteringOptions _filtering;
    private readonly IAiProviderFactory _factory;
    private readonly ILogger<AiDecisionPipeline> _logger;

    public AiDecisionPipeline(
        IOptions<AiOptions> aiOptions,
        IOptions<FilteringOptions> filteringOptions,
        IAiProviderFactory factory,
        ILogger<AiDecisionPipeline> logger)
    {
        _aiOptions = aiOptions.Value;
        _filtering = filteringOptions.Value;
        _factory = factory;
        _logger = logger;
    }

    public Task<AiDecision?> EvaluateScreeningAsync(
        string title, string excerpt, CancellationToken ct = default)
    {
        var prompt = PromptBuilder.BuildScreeningPrompt(title, excerpt, _filtering);
        return RunChainAsync("screening", _aiOptions.GetScreeningChain(), prompt, ct);
    }

    public Task<AiDecision?> EvaluateReviewAsync(
        string title, string fullContent, CancellationToken ct = default)
    {
        var prompt = PromptBuilder.BuildReviewPrompt(title, fullContent, _filtering);
        return RunChainAsync("review", _aiOptions.GetReviewChain(), prompt, ct);
    }

    private async Task<AiDecision?> RunChainAsync(
        string stageName,
        IReadOnlyList<string> chain,
        string prompt,
        CancellationToken ct)
    {
        if (chain.Count == 0)
        {
            _logger.LogError("The {Stage} chain is empty — check AI configuration", stageName);
            return null;
        }

        foreach (var providerName in chain)
        {
            ct.ThrowIfCancellationRequested();

            var provider = _factory.Get(providerName);
            if (provider is null)
            {
                _logger.LogWarning(
                    "Skipping unavailable provider '{Name}' in {Stage} chain", providerName, stageName);
                continue;
            }

            _logger.LogDebug(
                "Running {Stage} with provider '{Provider}' (model: {Model})",
                stageName, provider.InstanceName, provider.Model);

            var decision = await provider.EvaluateAsync(prompt, ct);

            if (decision is not null)
            {
                _logger.LogDebug(
                    "{Stage} decision from '{Provider}': passed={Passed}, reason={Reason}",
                    stageName, provider.InstanceName, decision.Passed, decision.Reason);
                return decision;
            }

            _logger.LogWarning(
                "Provider '{Name}' failed in {Stage} chain; trying next provider",
                providerName, stageName);
        }

        _logger.LogWarning("All providers in the {Stage} chain failed", stageName);
        return null;
    }
}
