using System.ComponentModel.DataAnnotations;
using System.Globalization;
using FeedTriage.Worker;
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
    public void AppLoggingOptions_DefaultsToInformation()
    {
        var opts = new AppLoggingOptions();

        Assert.Equal("Information", opts.Level);
        Assert.True(AppLoggingOptions.TryParseLevel(opts.Level, out var level));
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Information, level);
    }

    [Theory]
    [InlineData("verbose", Microsoft.Extensions.Logging.LogLevel.Debug)]
    [InlineData("debug", Microsoft.Extensions.Logging.LogLevel.Debug)]
    [InlineData("trace", Microsoft.Extensions.Logging.LogLevel.Trace)]
    [InlineData("info", Microsoft.Extensions.Logging.LogLevel.Information)]
    [InlineData("warn", Microsoft.Extensions.Logging.LogLevel.Warning)]
    [InlineData("fatal", Microsoft.Extensions.Logging.LogLevel.Critical)]
    [InlineData("none", Microsoft.Extensions.Logging.LogLevel.None)]
    public void AppLoggingOptions_ParsesAliases(string configuredLevel, Microsoft.Extensions.Logging.LogLevel expectedLevel)
    {
        Assert.True(AppLoggingOptions.TryParseLevel(configuredLevel, out var level));
        Assert.Equal(expectedLevel, level);
    }

    [Fact]
    public void AppLoggingOptions_RejectsInvalidLevels()
    {
        Assert.False(AppLoggingOptions.TryParseLevel("chatty", out _));
    }

    [Fact]
    public void SchedulerOptions_DefaultsToRunOnStart_AndFiveMinuteInterval()
    {
        var opts = new SchedulerOptions();
        Assert.True(opts.RunOnStart);
        Assert.Equal(TimeSpan.FromMinutes(5), opts.RunInterval);
    }

    [Fact]
    public void StateOptions_DefaultsToJsonStateFile_AndSqliteScoreDatabase()
    {
        var opts = new StateOptions();

        Assert.Equal("./data/state.json", opts.FilePath);
        Assert.Equal("./data/scores.db", opts.ScoreDatabasePath);
    }

    [Fact]
    public void ConsoleLoggingDefaults_UsesValidTimestampFormat()
    {
        const string brokenTimestampFormat = "yyyy-MM-dd'T'HH':'mm':'ss.fffzzz': ";

        Assert.NotEqual(brokenTimestampFormat, ConsoleLoggingDefaults.TimestampFormat);
        Assert.Throws<FormatException>(() => DateTimeOffset.UnixEpoch.ToString(
            brokenTimestampFormat,
            CultureInfo.InvariantCulture));

        var formatted = DateTimeOffset.UnixEpoch.ToString(
            ConsoleLoggingDefaults.TimestampFormat,
            CultureInfo.InvariantCulture);

        Assert.Equal("1970-01-01T00:00:00.000+00:00: ", formatted);
    }
}
