using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RssSummarizer.Worker.Configuration;
using RssSummarizer.Worker.Interfaces;
using RssSummarizer.Worker.Models;

namespace RssSummarizer.Worker.Clients;

public sealed class MinifluxClient : IMinifluxClient
{
    private readonly HttpClient _http;
    private readonly ILogger<MinifluxClient> _logger;

    public MinifluxClient(HttpClient http, IOptions<MinifluxOptions> options, ILogger<MinifluxClient> logger)
    {
        _http = http;
        _logger = logger;

        _http.BaseAddress = new Uri(options.Value.BaseUrl.TrimEnd('/') + "/");
        _http.DefaultRequestHeaders.Add("X-Auth-Token", options.Value.ApiToken);
    }

    public async Task<IReadOnlyList<MinifluxEntry>> GetUnreadEntriesAsync(
        int? limit = null,
        DateTimeOffset? after = null,
        CancellationToken ct = default)
    {
        var pageSize = limit ?? 10000;

        var url = $"v1/entries?status=unread&limit={pageSize}&order=published_at&direction=asc";
        if (after.HasValue)
            url += $"&after={after.Value.ToUnixTimeSeconds()}";

        _logger.LogDebug(
            "Fetching unread entries from Miniflux — limit={Limit} after={After}",
            pageSize, after?.ToString("u") ?? "none");

        var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<MinifluxEntriesResponse>(ct);
        var entries = result?.Entries ?? [];

        _logger.LogInformation(
            "Fetched {Count} unread entries from Miniflux (published after {After})",
            entries.Count, after?.ToString("u") ?? "none");
        return entries;
    }

    public async Task<string?> FetchContentAsync(long entryId, CancellationToken ct = default)
    {
        _logger.LogDebug("Fetching full content for entry {EntryId}", entryId);
        try
        {
            var response = await _http.GetAsync(
                $"v1/entries/{entryId}/fetch-content?update_content=false", ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Miniflux content fetch for entry {EntryId} returned {StatusCode}",
                    entryId, (int)response.StatusCode);
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<MinifluxFetchContentResponse>(ct);
            return result?.Content;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to fetch content for entry {EntryId}", entryId);
            return null;
        }
    }

    public async Task MarkAsReadAsync(IEnumerable<long> entryIds, CancellationToken ct = default)
    {
        var ids = entryIds.ToList();
        if (ids.Count == 0) return;

        _logger.LogDebug("Marking {Count} entries as read", ids.Count);

        var payload = JsonSerializer.Serialize(new { entry_ids = ids, status = "read" });
        var content = new StringContent(payload, Encoding.UTF8, "application/json");

        var response = await _http.PutAsync("v1/entries", content, ct);
        response.EnsureSuccessStatusCode();
    }
}
