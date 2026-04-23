using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using FeedTriage.Worker.Configuration;
using FeedTriage.Worker.Interfaces;
using FeedTriage.Worker.Models;

namespace FeedTriage.Worker.Ai;

/// <summary>
/// AI provider adapter for Ollama.
/// Supports Ollama's documented native /api/chat endpoint and preserves legacy
/// OpenAI-compatible /v1/chat/completions support for older configurations.
/// </summary>
public sealed class OllamaProvider : IAiProvider
{
    private readonly HttpClient _http;
    private readonly ILogger _logger;
    private readonly string _model;
    private readonly bool _usesNativeApi;

    public string InstanceName { get; }
    public string Model => _model;

    public OllamaProvider(
        string instanceName,
        ProviderOptions options,
        IHttpClientFactory httpClientFactory,
        ILogger logger)
    {
        InstanceName = instanceName;
        _model = options.Model;
        _logger = logger;

        _http = httpClientFactory.CreateClient($"ollama_{instanceName}");
        _http.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
        _http.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        _usesNativeApi = _http.BaseAddress.AbsolutePath.Contains("/api", StringComparison.OrdinalIgnoreCase);
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", options.ApiKey);
    }

    public async Task<AiDecision?> EvaluateAsync(string prompt, CancellationToken ct = default)
    {
        try
        {
            var response = _usesNativeApi
                ? await SendNativeApiRequestAsync(prompt, ct)
                : await SendLegacyRequestAsync(prompt, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning(
                    "Ollama provider {Instance} returned HTTP {Status}: {Body}",
                    InstanceName, (int)response.StatusCode, errorBody);
                return null;
            }

            var rawContent = _usesNativeApi
                ? await ReadNativeResponseAsync(response, ct)
                : await ReadLegacyResponseAsync(response, ct);

            if (string.IsNullOrWhiteSpace(rawContent))
            {
                _logger.LogWarning(
                    "Ollama provider {Instance} returned empty content", InstanceName);
                return null;
            }

            return ParseDecision(rawContent);
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Ollama provider {Instance} timed out", InstanceName);
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ollama provider {Instance} failed unexpectedly", InstanceName);
            return null;
        }
    }

    private async Task<HttpResponseMessage> SendNativeApiRequestAsync(string prompt, CancellationToken ct)
    {
        var requestBody = new OllamaNativeChatRequest
        {
            Model = _model,
            Messages = [new OllamaMessage { Role = "user", Content = prompt }],
            Stream = false,
            Format = "json"
        };

        var json = JsonSerializer.Serialize(
            requestBody, OllamaSerializerContext.Default.OllamaNativeChatRequest);
        var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

        return await _http.PostAsync("chat", httpContent, ct);
    }

    private async Task<HttpResponseMessage> SendLegacyRequestAsync(string prompt, CancellationToken ct)
    {
        var requestBody = new OllamaLegacyChatRequest
        {
            Model = _model,
            Messages = [new OllamaMessage { Role = "user", Content = prompt }],
            Stream = false,
            Temperature = 0.1,
            ResponseFormat = new OllamaLegacyResponseFormat { Type = "json_object" }
        };

        var json = JsonSerializer.Serialize(
            requestBody, OllamaSerializerContext.Default.OllamaLegacyChatRequest);
        var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

        return await _http.PostAsync("v1/chat/completions", httpContent, ct);
    }

    private async Task<string?> ReadNativeResponseAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var chatResponse = await response.Content.ReadFromJsonAsync(
            OllamaSerializerContext.Default.OllamaNativeChatResponse, ct);

        return chatResponse?.Message?.Content;
    }

    private async Task<string?> ReadLegacyResponseAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var chatResponse = await response.Content.ReadFromJsonAsync(
            OllamaSerializerContext.Default.OllamaLegacyChatResponse, ct);

        return chatResponse?.Choices?.FirstOrDefault()?.Message?.Content;
    }

    private AiDecision? ParseDecision(string content)
    {
        // Strip code fences defensively in case the model ignores response_format
        var json = content.Trim();
        if (json.StartsWith("```"))
        {
            var start = json.IndexOf('{');
            var end = json.LastIndexOf('}');
            if (start >= 0 && end > start)
                json = json[start..(end + 1)];
        }

        try
        {
            var parsed = JsonSerializer.Deserialize(
                json, OllamaSerializerContext.Default.AiRawDecision);

            if (parsed is null)
            {
                _logger.LogWarning(
                    "Ollama provider {Instance} returned null after parse", InstanceName);
                return null;
            }

            return new AiDecision
            {
                Passed = parsed.Passed,
                Reason = parsed.Reason ?? string.Empty,
                ProviderInstance = InstanceName,
                Model = _model
            };
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(
                ex, "Ollama provider {Instance} returned malformed JSON: {Content}",
                InstanceName, content);
            return null;
        }
    }
}

// ── Request DTOs ───────────────────────────────────────────────────────────────

internal sealed class OllamaNativeChatRequest
{
    [JsonPropertyName("model")] public string Model { get; set; } = string.Empty;
    [JsonPropertyName("messages")] public List<OllamaMessage> Messages { get; set; } = [];
    [JsonPropertyName("stream")] public bool Stream { get; set; } = false;
    [JsonPropertyName("format")] public string? Format { get; set; }
}

internal sealed class OllamaLegacyChatRequest
{
    [JsonPropertyName("model")] public string Model { get; set; } = string.Empty;
    [JsonPropertyName("messages")] public List<OllamaMessage> Messages { get; set; } = [];
    [JsonPropertyName("stream")] public bool Stream { get; set; } = false;
    [JsonPropertyName("temperature")] public double Temperature { get; set; }
    [JsonPropertyName("response_format")] public OllamaLegacyResponseFormat? ResponseFormat { get; set; }
}

internal sealed class OllamaMessage
{
    [JsonPropertyName("role")] public string Role { get; set; } = string.Empty;
    [JsonPropertyName("content")] public string Content { get; set; } = string.Empty;
}

internal sealed class OllamaLegacyResponseFormat
{
    [JsonPropertyName("type")] public string Type { get; set; } = "json_object";
}

// ── Response DTOs ─────────────────────────────────────────────────────────────

internal sealed class OllamaNativeChatResponse
{
    [JsonPropertyName("message")] public OllamaMessage? Message { get; set; }
}

internal sealed class OllamaLegacyChatResponse
{
    [JsonPropertyName("choices")] public List<OllamaLegacyChoice>? Choices { get; set; }
}

internal sealed class OllamaLegacyChoice
{
    [JsonPropertyName("message")] public OllamaMessage? Message { get; set; }
    [JsonPropertyName("finish_reason")] public string? FinishReason { get; set; }
}

/// <summary>Normalised shape of the JSON the model is instructed to return.</summary>
internal sealed class AiRawDecision
{
    [JsonPropertyName("passed")] public bool Passed { get; set; }
    [JsonPropertyName("reason")] public string? Reason { get; set; }
}

[JsonSerializable(typeof(OllamaNativeChatRequest))]
[JsonSerializable(typeof(OllamaNativeChatResponse))]
[JsonSerializable(typeof(OllamaLegacyChatRequest))]
[JsonSerializable(typeof(OllamaLegacyChatResponse))]
[JsonSerializable(typeof(AiRawDecision))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class OllamaSerializerContext : JsonSerializerContext { }
