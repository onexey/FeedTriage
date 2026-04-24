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
    public void MinifluxOptions_Defaults_BaseUrl_WhenMissing()
    {
        var opts = new MinifluxOptions { ApiToken = "token" };
        var results = Validate(opts);
        Assert.Empty(results);
        Assert.Equal("http://miniflux:8080", opts.BaseUrl);
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
        var opts = new FilteringOptions();
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
    public void AiOptions_Defaults_StandardChains_WhenMissing()
    {
        var opts = new AiOptions();
        var results = Validate(opts);
        Assert.Empty(results);
        Assert.Equal("screen_ollama_small", opts.ScreeningChain);
        Assert.Equal("review_ollama_large", opts.ReviewChain);
    }

    [Fact]
    public void AiOptions_Defaults_StandardProviders_WhenMissing()
    {
        var opts = new AiOptions();

        Assert.Equal("ollama", opts.Providers["screen_ollama_small"].Type);
        Assert.Equal("https://ollama.com/api", opts.Providers["screen_ollama_small"].BaseUrl);
        Assert.Equal("ministral-3:3b", opts.Providers["screen_ollama_small"].Model);
        Assert.Equal(60, opts.Providers["screen_ollama_small"].TimeoutSeconds);

        Assert.Equal("ollama", opts.Providers["review_ollama_large"].Type);
        Assert.Equal("https://ollama.com/api", opts.Providers["review_ollama_large"].BaseUrl);
        Assert.Equal("gemma3:27b", opts.Providers["review_ollama_large"].Model);
        Assert.Equal(180, opts.Providers["review_ollama_large"].TimeoutSeconds);
    }

    [Fact]
    public void ProcessingOptions_DefaultsToFiveMaxArticles_AndFalseDryRun()
    {
        var opts = new ProcessingOptions();
        Assert.Equal(5, opts.MaxArticlesPerRun);
        Assert.False(opts.DryRun);
    }

    [Fact]
    public void SchedulerOptions_DefaultsToRunOnStart_AndFiveMinuteInterval()
    {
        var opts = new SchedulerOptions();
        Assert.True(opts.RunOnStart);
        Assert.Equal(TimeSpan.FromMinutes(5), opts.RunInterval);
    }
}
