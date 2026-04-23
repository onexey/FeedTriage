using System.ComponentModel.DataAnnotations;
using FeedTriage.Worker.Configuration;
using Xunit;

namespace FeedTriage.Tests;

/// <summary>
/// Verifies that missing or invalid configuration causes validation failures at startup.
/// These tests exercise the DataAnnotations validation on each options class directly.
/// </summary>
public sealed class ConfigurationValidationTests
{
    private static IList<ValidationResult> Validate(object opts)
    {
        var ctx = new ValidationContext(opts);
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(opts, ctx, results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void MinifluxOptions_Fails_WhenBaseUrlMissing()
    {
        var opts = new MinifluxOptions { ApiToken = "token" };
        var results = Validate(opts);
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(MinifluxOptions.BaseUrl)));
    }

    [Fact]
    public void MinifluxOptions_Fails_WhenApiTokenMissing()
    {
        var opts = new MinifluxOptions { BaseUrl = "http://miniflux" };
        var results = Validate(opts);
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(MinifluxOptions.ApiToken)));
    }

    [Fact]
    public void MinifluxOptions_Passes_WhenValid()
    {
        var opts = new MinifluxOptions { BaseUrl = "http://miniflux.local", ApiToken = "tok" };
        var results = Validate(opts);
        Assert.Empty(results);
    }

    [Fact]
    public void FilteringOptions_Fails_WhenFocusTopicsMissing()
    {
        var opts = new FilteringOptions(); // FocusTopics defaults to empty string
        var results = Validate(opts);
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(FilteringOptions.FocusTopics)));
    }

    [Fact]
    public void FilteringOptions_Passes_WhenFocusTopicsSet()
    {
        var opts = new FilteringOptions { FocusTopics = "software engineering" };
        var results = Validate(opts);
        Assert.Empty(results);
    }

    [Fact]
    public void FilteringOptions_GetFocusTopicList_SplitsCommaList()
    {
        var opts = new FilteringOptions { FocusTopics = "software engineering, team leadership, architecture" };
        var list = opts.GetFocusTopicList();
        Assert.Equal(3, list.Count);
        Assert.Contains("software engineering", list);
        Assert.Contains("team leadership", list);
        Assert.Contains("architecture", list);
    }

    [Fact]
    public void FilteringOptions_GetAntiTopicList_ReturnsEmpty_WhenNotSet()
    {
        var opts = new FilteringOptions { FocusTopics = "software" };
        var list = opts.GetAntiTopicList();
        Assert.Empty(list);
    }

    [Fact]
    public void AiOptions_Fails_WhenScreeningChainMissing()
    {
        var opts = new AiOptions { ReviewChain = "p1" };
        var results = Validate(opts);
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(AiOptions.ScreeningChain)));
    }

    [Fact]
    public void AiOptions_Fails_WhenReviewChainMissing()
    {
        var opts = new AiOptions { ScreeningChain = "p1" };
        var results = Validate(opts);
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(AiOptions.ReviewChain)));
    }

    [Fact]
    public void ProcessingOptions_DefaultsToNullMaxArticles_AndFalseDryRun()
    {
        var opts = new ProcessingOptions();
        Assert.Null(opts.MaxArticlesPerRun);
        Assert.False(opts.DryRun);
    }

    [Fact]
    public void SchedulerOptions_DefaultsToRunOnStart_AndOneDayInterval()
    {
        var opts = new SchedulerOptions();
        Assert.True(opts.RunOnStart);
        Assert.Equal(TimeSpan.FromDays(1), opts.RunInterval);
    }
}
