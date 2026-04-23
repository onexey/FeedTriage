using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using FeedTriage.Worker;
using FeedTriage.Worker.Ai;
using FeedTriage.Worker.Clients;
using FeedTriage.Worker.Configuration;
using FeedTriage.Worker.Interfaces;
using FeedTriage.Worker.Services;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((_, configBuilder) =>
    {
        var normalizedEnv = Environment.GetEnvironmentVariables()
            .Cast<System.Collections.DictionaryEntry>()
            .Where(entry => entry.Key is string key && key.StartsWith("FEEDTRIAGE__", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                entry => NormalizeEnvKey((string)entry.Key),
                entry => entry.Value?.ToString(),
                StringComparer.OrdinalIgnoreCase);

        if (normalizedEnv.Count > 0)
        {
            configBuilder.AddInMemoryCollection(normalizedEnv);
        }
    })
    .ConfigureServices((ctx, services) =>
    {
        var config = ctx.Configuration;

        // ── Options registration with startup validation ───────────────────────
        services
            .AddOptions<SchedulerOptions>()
            .Bind(config.GetSection(SchedulerOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services
            .AddOptions<MinifluxOptions>()
            .Bind(config.GetSection(MinifluxOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services
            .AddOptions<FilteringOptions>()
            .Bind(config.GetSection(FilteringOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services
            .AddOptions<ProcessingOptions>()
            .Bind(config.GetSection(ProcessingOptions.SectionName));

        services
            .AddOptions<StateOptions>()
            .Bind(config.GetSection(StateOptions.SectionName));

        services
            .AddOptions<AiOptions>()
            .Bind(config.GetSection(AiOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // ── HTTP clients ──────────────────────────────────────────────────────
        services.AddHttpClient<IMinifluxClient, MinifluxClient>();

        // Generic IHttpClientFactory used by OllamaProvider (one named client per instance)
        services.AddHttpClient();

        // ── AI layer ──────────────────────────────────────────────────────────
        services.AddSingleton<IAiProviderFactory, AiProviderFactory>();
        services.AddSingleton<IAiDecisionPipeline, AiDecisionPipeline>();

        // ── Business logic ────────────────────────────────────────────────────
        services.AddSingleton<IRunStateRepository, JsonRunStateRepository>();
        services.AddTransient<IEntryScreeningContentHandler, HackerNewsScreeningContentHandler>();
        services.AddTransient<IArticleProcessor, ArticleProcessor>();

        // ── Worker ────────────────────────────────────────────────────────────
        services.AddHostedService<Worker>();
    })
    .Build();

host.Run();

static string NormalizeEnvKey(string envKey)
{
    var segments = envKey.Split("__", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    if (segments.Length == 0)
    {
        return envKey;
    }

    segments[0] = "FeedTriage";
    segments[^1] = segments[^1].Replace("_", string.Empty, StringComparison.Ordinal);

    return string.Join(':', segments);
}
