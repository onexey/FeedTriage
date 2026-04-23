using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RssSummarizer.Worker.Ai;
using RssSummarizer.Worker.Configuration;
using RssSummarizer.Worker.Interfaces;
using RssSummarizer.Worker.Models;
using Xunit;

namespace RssSummarizer.Tests;

public sealed class AiDecisionPipelineTests
{
    private static AiOptions MakeAiOptions(string screenChain = "p1", string reviewChain = "p2") =>
        new()
        {
            ScreeningChain = screenChain,
            ReviewChain = reviewChain,
            Providers = new Dictionary<string, ProviderOptions>(StringComparer.OrdinalIgnoreCase)
            {
                ["p1"] = new() { Type = "ollama", BaseUrl = "http://x", Model = "m1" },
                ["p2"] = new() { Type = "ollama", BaseUrl = "http://x", Model = "m2" },
                ["p3"] = new() { Type = "ollama", BaseUrl = "http://x", Model = "m3" },
            }
        };

    private static FilteringOptions MakeFiltering() =>
        new() { FocusTopics = "software engineering" };

    private static IAiDecisionPipeline CreatePipeline(
        AiOptions aiOpts,
        IAiProviderFactory factory) =>
        new AiDecisionPipeline(
            Options.Create(aiOpts),
            Options.Create(MakeFiltering()),
            factory,
            NullLogger<AiDecisionPipeline>.Instance);

    [Fact]
    public async Task EvaluateScreeningAsync_ReturnsFailed_WhenProviderReturnsFalse()
    {
        var provider = new Mock<IAiProvider>();
        provider.SetupGet(p => p.InstanceName).Returns("p1");
        provider.SetupGet(p => p.Model).Returns("m1");
        provider.Setup(p => p.EvaluateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiDecision
            {
                Passed = false,
                Reason = "Off-topic",
                ProviderInstance = "p1",
                Model = "m1"
            });

        var factory = MockFactory(new Dictionary<string, IAiProvider> { ["p1"] = provider.Object });
        var pipeline = CreatePipeline(MakeAiOptions(), factory);

        var decision = await pipeline.EvaluateScreeningAsync("title", "excerpt");

        Assert.NotNull(decision);
        Assert.False(decision.Passed);
        Assert.Equal("Off-topic", decision.Reason);
    }

    [Fact]
    public async Task EvaluateScreeningAsync_FallsBack_WhenFirstProviderFails()
    {
        var failingProvider = new Mock<IAiProvider>();
        failingProvider.SetupGet(p => p.InstanceName).Returns("p1");
        failingProvider.SetupGet(p => p.Model).Returns("m1");
        failingProvider.Setup(p => p.EvaluateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AiDecision?)null);

        var successProvider = new Mock<IAiProvider>();
        successProvider.SetupGet(p => p.InstanceName).Returns("p2");
        successProvider.SetupGet(p => p.Model).Returns("m2");
        successProvider.Setup(p => p.EvaluateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiDecision
            {
                Passed = true, Reason = "Good", ProviderInstance = "p2", Model = "m2"
            });

        var aiOpts = MakeAiOptions(screenChain: "p1,p2");
        var factory = MockFactory(new Dictionary<string, IAiProvider>
        {
            ["p1"] = failingProvider.Object,
            ["p2"] = successProvider.Object
        });
        var pipeline = CreatePipeline(aiOpts, factory);

        var decision = await pipeline.EvaluateScreeningAsync("title", "excerpt");

        Assert.NotNull(decision);
        Assert.True(decision.Passed);
        Assert.Equal("p2", decision.ProviderInstance);
    }

    [Fact]
    public async Task EvaluateScreeningAsync_ReturnsNull_WhenAllProvidersFail()
    {
        var p = new Mock<IAiProvider>();
        p.SetupGet(x => x.InstanceName).Returns("p1");
        p.SetupGet(x => x.Model).Returns("m1");
        p.Setup(x => x.EvaluateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AiDecision?)null);

        var factory = MockFactory(new Dictionary<string, IAiProvider> { ["p1"] = p.Object });
        var pipeline = CreatePipeline(MakeAiOptions(), factory);

        var decision = await pipeline.EvaluateScreeningAsync("title", "excerpt");

        Assert.Null(decision);
    }

    [Fact]
    public async Task EvaluateReviewAsync_UsesReviewChain_Independently()
    {
        var reviewProvider = new Mock<IAiProvider>();
        reviewProvider.SetupGet(p => p.InstanceName).Returns("p2");
        reviewProvider.SetupGet(p => p.Model).Returns("m2");
        reviewProvider.Setup(p => p.EvaluateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiDecision
            {
                Passed = true, Reason = "Excellent", ProviderInstance = "p2", Model = "m2"
            });

        var factory = MockFactory(new Dictionary<string, IAiProvider> { ["p2"] = reviewProvider.Object });
        var pipeline = CreatePipeline(MakeAiOptions(screenChain: "p1", reviewChain: "p2"), factory);

        var decision = await pipeline.EvaluateReviewAsync("title", "full content");

        Assert.NotNull(decision);
        Assert.True(decision.Passed);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IAiProviderFactory MockFactory(Dictionary<string, IAiProvider> providers)
    {
        var mock = new Mock<IAiProviderFactory>();
        mock.Setup(f => f.Get(It.IsAny<string>()))
            .Returns((string name) =>
                providers.TryGetValue(name, out var p) ? p : null);
        return mock.Object;
    }
}
