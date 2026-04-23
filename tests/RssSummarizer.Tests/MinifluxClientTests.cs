using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RssSummarizer.Tests.Helpers;
using RssSummarizer.Worker.Clients;
using RssSummarizer.Worker.Configuration;
using Xunit;

namespace RssSummarizer.Tests;

public sealed class MinifluxClientTests
{
    private static MinifluxClient CreateClient(FakeHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        var options = Options.Create(new MinifluxOptions
        {
            BaseUrl = "http://miniflux.test",
            ApiToken = "test-token"
        });
        return new MinifluxClient(httpClient, options, NullLogger<MinifluxClient>.Instance);
    }

    [Fact]
    public async Task GetUnreadEntriesAsync_ReturnsParsedEntries()
    {
        var handler = new FakeHttpMessageHandler();
        handler.On(HttpMethod.Get, "entries?status=unread", HttpStatusCode.OK, """
            {
              "total": 2,
              "entries": [
                {"id": 1, "title": "Entry One", "url": "https://example.com/1", "content": "<p>Hello</p>", "status": "unread"},
                {"id": 2, "title": "Entry Two", "url": "https://example.com/2", "content": "", "status": "unread"}
              ]
            }
            """);

        var client = CreateClient(handler);
        var entries = await client.GetUnreadEntriesAsync();

        Assert.Equal(2, entries.Count);
        Assert.Equal(1, entries[0].Id);
        Assert.Equal("Entry One", entries[0].Title);
        Assert.Equal("https://example.com/1", entries[0].Url);
    }

    [Fact]
    public async Task GetUnreadEntriesAsync_UsesRequestedLimit()
    {
        var handler = new FakeHttpMessageHandler();
        handler.On(HttpMethod.Get, "entries?status=unread&limit=3", HttpStatusCode.OK,
            """{"total": 1, "entries": [{"id": 1, "title": "Entry One", "url": "https://example.com/1", "content": "", "status": "unread"}]}""");

        var client = CreateClient(handler);
        var entries = await client.GetUnreadEntriesAsync(3);

        Assert.Single(entries);
        var call = Assert.Single(handler.Calls);
        Assert.Contains("limit=3", call.Uri?.ToString() ?? string.Empty);
    }

    [Fact]
    public async Task GetUnreadEntriesAsync_ReturnsEmpty_WhenNoEntries()
    {
        var handler = new FakeHttpMessageHandler();
        handler.On(HttpMethod.Get, "entries?status=unread", HttpStatusCode.OK,
            """{"total": 0, "entries": []}""");

        var client = CreateClient(handler);
        var entries = await client.GetUnreadEntriesAsync();

        Assert.Empty(entries);
    }

    [Fact]
    public async Task GetUnreadEntriesAsync_DefaultsToLargeLimit_WhenUnset()
    {
        var handler = new FakeHttpMessageHandler();
        handler.On(HttpMethod.Get, "entries?status=unread&limit=10000", HttpStatusCode.OK,
            """{"total": 0, "entries": []}""");

        var client = CreateClient(handler);
        await client.GetUnreadEntriesAsync();

        var call = Assert.Single(handler.Calls);
        Assert.Contains("limit=10000", call.Uri?.ToString() ?? string.Empty);
    }

    [Fact]
    public async Task FetchContentAsync_ReturnsContent_OnSuccess()
    {
        var handler = new FakeHttpMessageHandler();
        handler.On(HttpMethod.Get, "fetch-content", HttpStatusCode.OK,
            """{"content": "<p>Full article text</p>"}""");

        var client = CreateClient(handler);
        var content = await client.FetchContentAsync(42);

        Assert.Equal("<p>Full article text</p>", content);
    }

    [Fact]
    public async Task FetchContentAsync_ReturnsNull_OnHttpError()
    {
        var handler = new FakeHttpMessageHandler();
        handler.On(HttpMethod.Get, "fetch-content", HttpStatusCode.InternalServerError, "error");

        var client = CreateClient(handler);
        var content = await client.FetchContentAsync(42);

        Assert.Null(content);
    }

    [Fact]
    public async Task MarkAsReadAsync_SendsCorrectPayload()
    {
        var handler = new FakeHttpMessageHandler();
        handler.On(HttpMethod.Put, "v1/entries", HttpStatusCode.NoContent, "");

        var client = CreateClient(handler);
        await client.MarkAsReadAsync([1, 2, 3]);

        var call = Assert.Single(handler.Calls);
        Assert.Equal(HttpMethod.Put, call.Method);
        Assert.Contains("entry_ids", call.Body ?? "");
        Assert.Contains("read", call.Body ?? "");
    }

    [Fact]
    public async Task MarkAsReadAsync_DoesNothing_ForEmptyList()
    {
        var handler = new FakeHttpMessageHandler();
        var client = CreateClient(handler);
        await client.MarkAsReadAsync([]);

        Assert.Empty(handler.Calls);
    }
}
