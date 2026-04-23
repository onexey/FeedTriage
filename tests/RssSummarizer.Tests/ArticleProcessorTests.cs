using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RssSummarizer.Tests.Helpers;
using RssSummarizer.Worker.Configuration;
using RssSummarizer.Worker.Interfaces;
using RssSummarizer.Worker.Models;
using RssSummarizer.Worker.Services;
using Xunit;

namespace RssSummarizer.Tests;

public sealed class ArticleProcessorTests
{
    private static readonly MinifluxEntry SampleEntry = new()
    {
        Id = 42,
        Title = "Great Software Article",
        Url = "https://example.com/article",
        Content = "<p>This article is about software architecture patterns.</p>"
    };

    private static readonly AiDecision PassedDecision = new()
    {
        Passed = true, Reason = "Relevant", ProviderInstance = "test", Model = "test-model"
    };

    private static readonly AiDecision FailedDecision = new()
    {
        Passed = false, Reason = "Off-topic", ProviderInstance = "test", Model = "test-model"
    };

    private static ArticleProcessor CreateProcessor(
        IMinifluxClient? miniflux = null,
        IAiDecisionPipeline? ai = null,
        IRunStateRepository? state = null,
        IEnumerable<IEntryScreeningContentHandler>? screeningContentHandlers = null,
        bool dryRun = false,
        int? maxArticles = null)
    {
        var minifluxMock = miniflux ?? DefaultMiniflux();
        var aiMock = ai ?? DefaultAi();
        var stateMock = state ?? DefaultState();
        var handlers = screeningContentHandlers ?? [];
        var opts = Options.Create(new ProcessingOptions { DryRun = dryRun, MaxArticlesPerRun = maxArticles });
        return new ArticleProcessor(minifluxMock, aiMock, stateMock, handlers, opts, NullLogger<ArticleProcessor>.Instance);
    }

    private static IMinifluxClient DefaultMiniflux(
        MinifluxEntry? entry = null,
        string? fetchedContent = "<p>Full content</p>")
    {
        var mock = new Mock<IMinifluxClient>();
        mock.Setup(m => m.GetUnreadEntriesAsync(It.IsAny<int?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MinifluxEntry> { entry ?? SampleEntry });
        mock.Setup(m => m.FetchContentAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fetchedContent);
        mock.Setup(m => m.MarkAsReadAsync(It.IsAny<IEnumerable<long>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return mock.Object;
    }

    private static IRunStateRepository DefaultState()
    {
        var mock = new Mock<IRunStateRepository>();
        mock.Setup(s => s.GetLastPublishedAtAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((DateTimeOffset?)null);
        mock.Setup(s => s.SaveLastPublishedAtAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return mock.Object;
    }

    private static IAiDecisionPipeline DefaultAi(
        AiDecision? screenDecision = null,
        AiDecision? reviewDecision = null)
    {
        var mock = new Mock<IAiDecisionPipeline>();
        mock.Setup(a => a.EvaluateScreeningAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(screenDecision ?? PassedDecision);
        mock.Setup(a => a.EvaluateReviewAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(reviewDecision ?? PassedDecision);
        return mock.Object;
    }

    [Fact]
    public async Task Stage1ReturnsFalse_MarksReadWithoutStage2()
    {
        var minifluxMock = new Mock<IMinifluxClient>();
        minifluxMock.Setup(m => m.GetUnreadEntriesAsync(It.IsAny<int?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MinifluxEntry> { SampleEntry });
        minifluxMock.Setup(m => m.MarkAsReadAsync(It.IsAny<IEnumerable<long>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var ai = DefaultAi(screenDecision: FailedDecision, reviewDecision: null);

        var processor = CreateProcessor(minifluxMock.Object, ai: ai);
        var summary = await processor.ProcessAsync();

        Assert.Single(summary.Results);
        var result = summary.Results[0];
        Assert.False(result.ScreeningPassed);
        Assert.Null(result.ReviewPassed);
        Assert.Empty(result.RelevantUrls);
        Assert.True(result.MarkedAsRead);

        var aiMock = Mock.Get(ai);
        aiMock.Verify(a => a.EvaluateReviewAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Stage1True_Stage2True_LeavesUnreadAndTracksRelevantUrl()
    {
        var minifluxMock = new Mock<IMinifluxClient>();
        minifluxMock.Setup(m => m.GetUnreadEntriesAsync(It.IsAny<int?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MinifluxEntry> { SampleEntry });
        minifluxMock.Setup(m => m.FetchContentAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync("<p>Full content</p>");
        minifluxMock.Setup(m => m.MarkAsReadAsync(It.IsAny<IEnumerable<long>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var processor = CreateProcessor(minifluxMock.Object);
        var summary = await processor.ProcessAsync();

        var result = Assert.Single(summary.Results);
        Assert.True(result.ScreeningPassed);
        Assert.True(result.ReviewPassed);
        Assert.False(result.MarkedAsRead);
        Assert.Equal(new[] { SampleEntry.Url }, result.RelevantUrls);
        Assert.Equal(1, summary.RelevantMatches);
        Assert.Equal(0, summary.MarkedAsRead);
    }

    [Fact]
    public async Task Stage1True_Stage2False_MarksRead()
    {
        var processor = CreateProcessor(
            ai: DefaultAi(screenDecision: PassedDecision, reviewDecision: FailedDecision));

        var summary = await processor.ProcessAsync();

        var result = Assert.Single(summary.Results);
        Assert.True(result.ScreeningPassed);
        Assert.False(result.ReviewPassed);
        Assert.Empty(result.RelevantUrls);
        Assert.True(result.MarkedAsRead);
    }

    [Fact]
    public async Task AllStage1ProvidersFail_LeavesArticleUnread()
    {
        var aiMock = new Mock<IAiDecisionPipeline>();
        aiMock.Setup(a => a.EvaluateScreeningAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AiDecision?)null);
        var minifluxMock = new Mock<IMinifluxClient>();
        minifluxMock.Setup(m => m.GetUnreadEntriesAsync(It.IsAny<int?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MinifluxEntry> { SampleEntry });

        var processor = CreateProcessor(minifluxMock.Object, ai: aiMock.Object);
        var summary = await processor.ProcessAsync();

        var result = Assert.Single(summary.Results);
        Assert.Null(result.ScreeningPassed);
        Assert.False(result.MarkedAsRead);
        Assert.Empty(result.RelevantUrls);
        Assert.NotNull(result.ErrorMessage);

        minifluxMock.Verify(m => m.MarkAsReadAsync(It.IsAny<IEnumerable<long>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AllStage2ProvidersFail_LeavesArticleUnread()
    {
        var aiMock = new Mock<IAiDecisionPipeline>();
        aiMock.Setup(a => a.EvaluateScreeningAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PassedDecision);
        aiMock.Setup(a => a.EvaluateReviewAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AiDecision?)null);
        var minifluxMock = new Mock<IMinifluxClient>();
        minifluxMock.Setup(m => m.GetUnreadEntriesAsync(It.IsAny<int?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MinifluxEntry> { SampleEntry });
        minifluxMock.Setup(m => m.FetchContentAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("<p>content</p>");

        var processor = CreateProcessor(minifluxMock.Object, ai: aiMock.Object);
        var summary = await processor.ProcessAsync();

        var result = Assert.Single(summary.Results);
        Assert.True(result.ScreeningPassed);
        Assert.Null(result.ReviewPassed);
        Assert.False(result.MarkedAsRead);
        Assert.Empty(result.RelevantUrls);
    }

    [Fact]
    public async Task ContentFetchFailure_LeavesArticleUnread()
    {
        var minifluxMock = new Mock<IMinifluxClient>();
        minifluxMock.Setup(m => m.GetUnreadEntriesAsync(It.IsAny<int?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MinifluxEntry> { SampleEntry });
        minifluxMock.Setup(m => m.FetchContentAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var processor = CreateProcessor(minifluxMock.Object);
        var summary = await processor.ProcessAsync();

        var result = Assert.Single(summary.Results);
        Assert.True(result.ScreeningPassed);
        Assert.False(result.MarkedAsRead);
        Assert.Equal("Full-content fetch failed for article", result.ErrorMessage);
    }

    [Fact]
    public async Task DryRun_DoesNotMarkEntriesRead_OrAdvanceState()
    {
        var stateMock = new Mock<IRunStateRepository>();
        stateMock.Setup(s => s.GetLastPublishedAtAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((DateTimeOffset?)null);

        var minifluxMock = new Mock<IMinifluxClient>();
        minifluxMock.Setup(m => m.GetUnreadEntriesAsync(It.IsAny<int?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MinifluxEntry> { SampleEntry });
        minifluxMock.Setup(m => m.FetchContentAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("<p>Full content</p>");

        var processor = CreateProcessor(
            miniflux: minifluxMock.Object,
            ai: DefaultAi(screenDecision: PassedDecision, reviewDecision: FailedDecision),
            state: stateMock.Object,
            dryRun: true);
        var summary = await processor.ProcessAsync();

        var result = Assert.Single(summary.Results);
        Assert.False(result.ReviewPassed);
        Assert.False(result.MarkedAsRead);

        minifluxMock.Verify(m => m.MarkAsReadAsync(It.IsAny<IEnumerable<long>>(), It.IsAny<CancellationToken>()), Times.Never);
        stateMock.Verify(s => s.SaveLastPublishedAtAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MaxArticlesPerRun_LimitsFetchAndProcessing()
    {
        var entries = Enumerable.Range(1, 3)
            .Select(i => new MinifluxEntry
            {
                Id = i, Title = $"Article {i}",
                Url = $"https://example.com/{i}", Content = "<p>text</p>"
            })
            .ToList();

        var minifluxMock = new Mock<IMinifluxClient>();
        minifluxMock.Setup(m => m.GetUnreadEntriesAsync(3, It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entries);
        minifluxMock.Setup(m => m.FetchContentAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("<p>content</p>");
        minifluxMock.Setup(m => m.MarkAsReadAsync(It.IsAny<IEnumerable<long>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var processor = CreateProcessor(minifluxMock.Object, maxArticles: 3);
        var summary = await processor.ProcessAsync();

        Assert.Equal(3, summary.TotalFetched);
        Assert.Equal(3, summary.Results.Count);
    }

    [Fact]
    public async Task MarkAsReadFailure_AfterIrrelevantDecision_IsSurfacedInResult()
    {
        var minifluxMock = new Mock<IMinifluxClient>();
        minifluxMock.Setup(m => m.GetUnreadEntriesAsync(It.IsAny<int?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MinifluxEntry> { SampleEntry });
        minifluxMock.Setup(m => m.FetchContentAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("<p>content</p>");
        minifluxMock.Setup(m => m.MarkAsReadAsync(It.IsAny<IEnumerable<long>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Miniflux unreachable"));

        var processor = CreateProcessor(
            miniflux: minifluxMock.Object,
            ai: DefaultAi(screenDecision: PassedDecision, reviewDecision: FailedDecision));
        var summary = await processor.ProcessAsync();

        var result = Assert.Single(summary.Results);
        Assert.False(result.MarkedAsRead);
        Assert.Empty(result.RelevantUrls);
    }

    [Fact]
    public async Task HackerNewsEntries_EvaluateArticleAndDiscussionSeparately()
    {
        var hackerNewsEntry = new MinifluxEntry
        {
            Id = 42,
            Title = "Interesting review workflow",
            Url = "https://example.com/article",
            CommentsUrl = "https://news.ycombinator.com/item?id=123",
            Content = "<a href=\"https://news.ycombinator.com/item?id=123\">Comments</a>"
        };

        var minifluxMock = new Mock<IMinifluxClient>();
        minifluxMock.Setup(m => m.GetUnreadEntriesAsync(It.IsAny<int?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MinifluxEntry> { hackerNewsEntry });
        minifluxMock.Setup(m => m.FetchContentAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync("<p>This article is about software architecture and code review workflows.</p>");
        minifluxMock.Setup(m => m.MarkAsReadAsync(It.IsAny<IEnumerable<long>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var commentsHandler = new FakeHttpMessageHandler();
        commentsHandler.On(HttpMethod.Get, "news.ycombinator.com/item", System.Net.HttpStatusCode.OK,
            "<html><body><p>Comments discuss code review adoption and team process improvements.</p></body></html>",
            "text/html");

        var httpClientFactory = new FakeHttpClientFactory(commentsHandler);
        var screeningHandler = new HackerNewsScreeningContentHandler(
            minifluxMock.Object,
            httpClientFactory,
            NullLogger<HackerNewsScreeningContentHandler>.Instance);

        var aiMock = new Mock<IAiDecisionPipeline>();
        aiMock.Setup(a => a.EvaluateScreeningAsync(
                hackerNewsEntry.Title,
                It.Is<string>(content =>
                    content.Contains("Article excerpt:")
                    && content.Contains("software architecture and code review workflows")
                    && !content.Contains("Discussion excerpt:")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(FailedDecision);
        aiMock.Setup(a => a.EvaluateScreeningAsync(
                $"{hackerNewsEntry.Title} (Hacker News discussion)",
                It.Is<string>(content =>
                    content.Contains("Discussion excerpt:")
                    && content.Contains("team process improvements")
                    && !content.Contains("Article excerpt:")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(FailedDecision);

        var processor = CreateProcessor(
            miniflux: minifluxMock.Object,
            ai: aiMock.Object,
            screeningContentHandlers: [screeningHandler]);

        await processor.ProcessAsync();

        aiMock.VerifyAll();
    }

    [Fact]
    public async Task HackerNewsEntries_KeepOnlyRelevantCandidateUrls()
    {
        var hackerNewsEntry = new MinifluxEntry
        {
            Id = 42,
            Title = "Interesting review workflow",
            Url = "https://example.com/article",
            CommentsUrl = "https://news.ycombinator.com/item?id=123",
            Content = "<a href=\"https://news.ycombinator.com/item?id=123\">Comments</a>"
        };

        var minifluxMock = new Mock<IMinifluxClient>();
        minifluxMock.Setup(m => m.GetUnreadEntriesAsync(It.IsAny<int?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MinifluxEntry> { hackerNewsEntry });
        minifluxMock.Setup(m => m.FetchContentAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync("<p>This article is about software architecture and code review workflows.</p>");
        minifluxMock.Setup(m => m.MarkAsReadAsync(It.IsAny<IEnumerable<long>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var commentsHandler = new FakeHttpMessageHandler();
        commentsHandler.On(HttpMethod.Get, "news.ycombinator.com/item", System.Net.HttpStatusCode.OK,
            "<html><body><p>Comments discuss startup fundraising instead of engineering leadership.</p></body></html>",
            "text/html");

        var aiMock = new Mock<IAiDecisionPipeline>();
        aiMock.Setup(a => a.EvaluateScreeningAsync(hackerNewsEntry.Title, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PassedDecision);
        aiMock.Setup(a => a.EvaluateScreeningAsync($"{hackerNewsEntry.Title} (Hacker News discussion)", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PassedDecision);
        aiMock.Setup(a => a.EvaluateReviewAsync(hackerNewsEntry.Title, It.Is<string>(content => content.Contains("code review workflows")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PassedDecision);
        aiMock.Setup(a => a.EvaluateReviewAsync($"{hackerNewsEntry.Title} (Hacker News discussion)", It.Is<string>(content => content.Contains("startup fundraising")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(FailedDecision);

        var screeningHandler = new HackerNewsScreeningContentHandler(
            minifluxMock.Object,
            new FakeHttpClientFactory(commentsHandler),
            NullLogger<HackerNewsScreeningContentHandler>.Instance);

        var processor = CreateProcessor(
            miniflux: minifluxMock.Object,
            ai: aiMock.Object,
            screeningContentHandlers: [screeningHandler]);

        var summary = await processor.ProcessAsync();

        var result = Assert.Single(summary.Results);
        Assert.Equal(new[] { hackerNewsEntry.Url }, result.RelevantUrls);
        Assert.False(result.MarkedAsRead);
        Assert.Equal(1, summary.RelevantMatches);
    }

    [Fact]
    public async Task HackerNewsEntries_KeepBothMatchingCandidateUrls()
    {
        var hackerNewsEntry = new MinifluxEntry
        {
            Id = 42,
            Title = "Interesting review workflow",
            Url = "https://example.com/article",
            CommentsUrl = "https://news.ycombinator.com/item?id=123",
            Content = "<a href=\"https://news.ycombinator.com/item?id=123\">Comments</a>"
        };

        var minifluxMock = new Mock<IMinifluxClient>();
        minifluxMock.Setup(m => m.GetUnreadEntriesAsync(It.IsAny<int?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MinifluxEntry> { hackerNewsEntry });
        minifluxMock.Setup(m => m.FetchContentAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync("<p>This article is about software architecture and code review workflows.</p>");
        minifluxMock.Setup(m => m.MarkAsReadAsync(It.IsAny<IEnumerable<long>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var commentsHandler = new FakeHttpMessageHandler();
        commentsHandler.On(HttpMethod.Get, "news.ycombinator.com/item", System.Net.HttpStatusCode.OK,
            "<html><body><p>Comments discuss code review adoption and team process improvements.</p></body></html>",
            "text/html");

        var aiMock = new Mock<IAiDecisionPipeline>();
        aiMock.Setup(a => a.EvaluateScreeningAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PassedDecision);
        aiMock.Setup(a => a.EvaluateReviewAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PassedDecision);

        var screeningHandler = new HackerNewsScreeningContentHandler(
            minifluxMock.Object,
            new FakeHttpClientFactory(commentsHandler),
            NullLogger<HackerNewsScreeningContentHandler>.Instance);

        var processor = CreateProcessor(
            miniflux: minifluxMock.Object,
            ai: aiMock.Object,
            screeningContentHandlers: [screeningHandler]);

        var summary = await processor.ProcessAsync();

        var result = Assert.Single(summary.Results);
        Assert.Equal(2, result.RelevantUrls.Count);
        Assert.Contains(hackerNewsEntry.Url, result.RelevantUrls);
        Assert.Contains(hackerNewsEntry.CommentsUrl!, result.RelevantUrls);
        Assert.Equal(2, summary.RelevantMatches);
        Assert.False(result.MarkedAsRead);
    }

    [Fact]
    public async Task HackerNewsEntries_ReusePrefetchedArticleContentForReview()
    {
        var hackerNewsEntry = new MinifluxEntry
        {
            Id = 42,
            Title = "Interesting review workflow",
            Url = "https://example.com/article",
            CommentsUrl = "https://news.ycombinator.com/item?id=123",
            Content = "<a href=\"https://news.ycombinator.com/item?id=123\">Comments</a>"
        };

        var minifluxMock = new Mock<IMinifluxClient>();
        minifluxMock.Setup(m => m.GetUnreadEntriesAsync(It.IsAny<int?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MinifluxEntry> { hackerNewsEntry });
        minifluxMock.Setup(m => m.FetchContentAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync("<p>This article is about software architecture and code review workflows.</p>");
        minifluxMock.Setup(m => m.MarkAsReadAsync(It.IsAny<IEnumerable<long>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var commentsHandler = new FakeHttpMessageHandler();
        commentsHandler.On(HttpMethod.Get, "news.ycombinator.com/item", System.Net.HttpStatusCode.OK,
            "<html><body><p>Comments discuss code review adoption.</p></body></html>",
            "text/html");

        var aiMock = new Mock<IAiDecisionPipeline>();
        aiMock.Setup(a => a.EvaluateScreeningAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PassedDecision);
        aiMock.Setup(a => a.EvaluateReviewAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(FailedDecision);

        var screeningHandler = new HackerNewsScreeningContentHandler(
            minifluxMock.Object,
            new FakeHttpClientFactory(commentsHandler),
            NullLogger<HackerNewsScreeningContentHandler>.Instance);

        var processor = CreateProcessor(
            miniflux: minifluxMock.Object,
            ai: aiMock.Object,
            screeningContentHandlers: [screeningHandler]);

        await processor.ProcessAsync();

        minifluxMock.Verify(m => m.FetchContentAsync(42, It.IsAny<CancellationToken>()), Times.Once);
    }
}
