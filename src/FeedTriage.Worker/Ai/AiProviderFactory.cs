using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FeedTriage.Worker.Configuration;
using FeedTriage.Worker.Interfaces;

namespace FeedTriage.Worker.Ai;

/// <summary>
/// Creates and caches <see cref="IAiProvider"/> instances by their configured name.
/// Adding support for a new provider type only requires a new case in <see cref="CreateProvider"/>.
/// </summary>
public sealed class AiProviderFactory : IAiProviderFactory
{
    private readonly AiOptions _aiOptions;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AiProviderFactory> _logger;
    private readonly ILoggerFactory _loggerFactory;

    // Cache provider instances; they are stateless and safe to reuse
    private readonly Dictionary<string, IAiProvider> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    public AiProviderFactory(
        IOptions<AiOptions> aiOptions,
        IHttpClientFactory httpClientFactory,
        ILogger<AiProviderFactory> logger,
        ILoggerFactory loggerFactory)
    {
        _aiOptions = aiOptions.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _loggerFactory = loggerFactory;
    }

    /// <summary>
    /// Returns the provider for <paramref name="instanceName"/>, or null if the
    /// name is not found in configuration or the provider type is unsupported.
    /// </summary>
    public IAiProvider? Get(string instanceName)
    {
        if (_cache.TryGetValue(instanceName, out var cached))
            return cached;

        if (!_aiOptions.Providers.TryGetValue(instanceName, out var opts))
        {
            _logger.LogWarning(
                "AI provider instance '{Name}' is referenced in a chain but not defined in Providers config",
                instanceName);
            return null;
        }

        var provider = CreateProvider(instanceName, opts);
        if (provider is not null)
            _cache[instanceName] = provider;

        return provider;
    }

    private IAiProvider? CreateProvider(string instanceName, ProviderOptions opts)
    {
        return opts.Type.ToLowerInvariant() switch
        {
            "ollama" => new OllamaProvider(
                instanceName,
                opts,
                _httpClientFactory,
                _loggerFactory.CreateLogger<OllamaProvider>()),

            _ => LogUnknownType(instanceName, opts.Type)
        };
    }

    private IAiProvider? LogUnknownType(string instanceName, string type)
    {
        _logger.LogError(
            "Provider instance '{Name}' has unknown type '{Type}'. Supported types: ollama",
            instanceName, type);
        return null;
    }
}
