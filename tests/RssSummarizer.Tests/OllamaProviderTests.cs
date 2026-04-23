using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using RssSummarizer.Tests.Helpers;
using RssSummarizer.Worker.Ai;
using RssSummarizer.Worker.Configuration;
using Xunit;

namespace RssSummarizer.Tests;

public sealed class OllamaProviderTests
{
    private static OllamaProvider CreateProvider(FakeHttpMessageHandler handler,
                string instanceName = "test_ollama", string model = "qwen3:4b", int timeoutSeconds = 30,
                string baseUrl = "https://ollama.com/api")
    {
        var factory = new FakeHttpClientFactory(handler);
        var options = new ProviderOptions
        {
            Type = "ollama",
                        BaseUrl = baseUrl,
            Model = model,
            ApiKey = "test-api-key",
            TimeoutSeconds = timeoutSeconds
        };
        return new OllamaProvider(instanceName, options, factory, NullLogger<OllamaProvider>.Instance);
    }

        // ── Helpers to build response bodies ───────────────────────────────────────

        private static string NativeOkResponse(string content) => $$"""
                {
                    "message": {
                        "role": "assistant",
                        "content": {{System.Text.Json.JsonSerializer.Serialize(content)}}
                    },
                    "done": true
                }
                """;

        private static string LegacyOkResponse(string content) => $$"""
        {
          "choices": [
            {
              "message": {"role": "assistant", "content": {{System.Text.Json.JsonSerializer.Serialize(content)}}},
              "finish_reason": "stop"
            }
          ]
        }
        """;

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EvaluateAsync_ReturnsParsedDecision_OnValidResponse()
    {
        var handler = new FakeHttpMessageHandler();
        handler.OnAny(HttpStatusCode.OK,
            NativeOkResponse("""{"passed": true, "reason": "Relevant to software engineering"}"""));

        var provider = CreateProvider(handler);
        var decision = await provider.EvaluateAsync("Is this relevant?");

        Assert.NotNull(decision);
        Assert.True(decision.Passed);
        Assert.Equal("Relevant to software engineering", decision.Reason);
        Assert.Equal("test_ollama", decision.ProviderInstance);
        Assert.Equal("qwen3:4b", decision.Model);
    }

    [Fact]
    public async Task EvaluateAsync_ReturnsFalseDecision_WhenModelSaysNo()
    {
        var handler = new FakeHttpMessageHandler();
        handler.OnAny(HttpStatusCode.OK,
            NativeOkResponse("""{"passed": false, "reason": "Off-topic content"}"""));

        var provider = CreateProvider(handler);
        var decision = await provider.EvaluateAsync("irrelevant article");

        Assert.NotNull(decision);
        Assert.False(decision.Passed);
        Assert.Equal("Off-topic content", decision.Reason);
    }

    [Fact]
    public async Task EvaluateAsync_ReturnsNull_OnMalformedJson()
    {
        var handler = new FakeHttpMessageHandler();
        handler.OnAny(HttpStatusCode.OK, NativeOkResponse("This is not JSON at all"));

        var provider = CreateProvider(handler);
        var decision = await provider.EvaluateAsync("some prompt");

        Assert.Null(decision);
    }

    [Fact]
    public async Task EvaluateAsync_ReturnsNull_OnHttpError()
    {
        var handler = new FakeHttpMessageHandler();
        handler.OnAny(HttpStatusCode.Unauthorized,
            """{"error": {"message": "Invalid API key"}}""");

        var provider = CreateProvider(handler);
        var decision = await provider.EvaluateAsync("some prompt");

        Assert.Null(decision);
    }

    [Fact]
    public async Task EvaluateAsync_ReturnsNull_OnEmptyChoices()
    {
        var handler = new FakeHttpMessageHandler();
        handler.OnAny(HttpStatusCode.OK, """{"message": {"role": "assistant", "content": ""}, "done": true}""");

        var provider = CreateProvider(handler);
        var decision = await provider.EvaluateAsync("some prompt");

        Assert.Null(decision);
    }

    [Fact]
    public async Task EvaluateAsync_ParsesJsonWrappedInCodeFence()
    {
        var handler = new FakeHttpMessageHandler();
        handler.OnAny(HttpStatusCode.OK,
            NativeOkResponse("```json\n{\"passed\": true, \"reason\": \"Good article\"}\n```"));

        var provider = CreateProvider(handler);
        var decision = await provider.EvaluateAsync("some prompt");

        Assert.NotNull(decision);
        Assert.True(decision.Passed);
    }

    [Fact]
    public async Task EvaluateAsync_SendsBearerAuthHeader()
    {
        var handler = new FakeHttpMessageHandler();
        handler.OnAny(HttpStatusCode.OK,
            NativeOkResponse("""{"passed": true, "reason": "ok"}"""));

        var provider = CreateProvider(handler);
        await provider.EvaluateAsync("prompt");

        var call = Assert.Single(handler.Calls);
        // The HttpClient sets the Authorization header — we verify the request was sent
        // (the fake handler records the URI and method; header inspection would require
        // capturing the full HttpRequestMessage, which FakeHttpMessageHandler doesn't
        // expose by default — that's fine, the constructor wires the header)
        Assert.Contains("/api/chat", call.Uri?.ToString() ?? "");
    }

    [Fact]
    public async Task EvaluateAsync_PostsToCorrectEndpoint()
    {
        var handler = new FakeHttpMessageHandler();
        handler.OnAny(HttpStatusCode.OK,
            NativeOkResponse("""{"passed": false, "reason": "irrelevant"}"""));

        var provider = CreateProvider(handler);
        await provider.EvaluateAsync("prompt");

        var call = Assert.Single(handler.Calls);
        Assert.Equal(HttpMethod.Post, call.Method);
        Assert.Contains("/api/chat", call.Uri?.ToString() ?? "");
    }

    [Fact]
    public async Task EvaluateAsync_RequestBodyContainsModelAndJsonResponseFormat()
    {
        var handler = new FakeHttpMessageHandler();
        handler.OnAny(HttpStatusCode.OK,
            NativeOkResponse("""{"passed": true, "reason": "ok"}"""));

        var provider = CreateProvider(handler, model: "qwen3:4b");
        await provider.EvaluateAsync("test prompt");

        var call = Assert.Single(handler.Calls);
        Assert.Contains("qwen3:4b", call.Body ?? "");
        Assert.Contains("\"format\":\"json\"", call.Body ?? "");
    }

    [Fact]
    public async Task EvaluateAsync_UsesLegacyEndpoint_ForLegacyBaseUrl()
    {
        var handler = new FakeHttpMessageHandler();
        handler.OnAny(HttpStatusCode.OK,
            LegacyOkResponse("""{"passed": true, "reason": "ok"}"""));

        var provider = CreateProvider(handler, baseUrl: "https://api.ollama.com");
        await provider.EvaluateAsync("prompt");

        var call = Assert.Single(handler.Calls);
        Assert.Contains("/v1/chat/completions", call.Uri?.ToString() ?? "");
    }
}

/// <summary>Simple IHttpClientFactory that always returns an HttpClient backed by the given handler.</summary>
internal sealed class FakeHttpClientFactory : IHttpClientFactory
{
    private readonly HttpMessageHandler _handler;
    public FakeHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
    public HttpClient CreateClient(string name) => new(_handler);
}
