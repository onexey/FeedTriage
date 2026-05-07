using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FeedTriage.Worker.Configuration;
using FeedTriage.Worker.Interfaces;

namespace FeedTriage.Worker.Services;

public sealed class JsonRunStateRepository : IRunStateRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _filePath;
    private readonly ILogger<JsonRunStateRepository> _logger;

    public JsonRunStateRepository(
        IOptions<StateOptions> options,
        ILogger<JsonRunStateRepository> logger)
    {
        _filePath = options.Value.FilePath;
        _logger = logger;
    }

    public async Task<DateTimeOffset?> GetLastPublishedAtAsync(CancellationToken ct = default)
    {
        try
        {
            if (await EnsureStateFileExistsAsync(ct))
            {
                _logger.LogInformation(
                    "Initialized empty state file at {Path} — starting from scratch (all unread entries)",
                    _filePath);
                return null;
            }

            var json = await File.ReadAllTextAsync(_filePath, ct);
            var state = JsonSerializer.Deserialize<RunState>(json, JsonOptions);
            _logger.LogInformation(
                "Loaded run state from {Path} — last published at: {PublishedAt}",
                _filePath, state?.LastPublishedAt);
            return state?.LastPublishedAt;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to read state file at {Path} — starting from scratch (all unread entries)", _filePath);
            return null;
        }
    }

    public async Task SaveLastPublishedAtAsync(DateTimeOffset publishedAt, CancellationToken ct = default)
    {
        try
        {
            await EnsureStateFileExistsAsync(ct);

            var state = new RunState { LastPublishedAt = publishedAt };
            var json = JsonSerializer.Serialize(state, JsonOptions);
            await File.WriteAllTextAsync(_filePath, json, ct);
            _logger.LogDebug(
                "Saved run state to {Path} — last published at: {PublishedAt}", _filePath, publishedAt);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to save state file at {Path}. " +
                "If running in Docker, mount a writable volume for ./data (container path /app/data).",
                _filePath);
        }
    }

    private async Task<bool> EnsureStateFileExistsAsync(CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        if (File.Exists(_filePath))
        {
            return false;
        }

        var json = JsonSerializer.Serialize(new RunState(), JsonOptions);
        await File.WriteAllTextAsync(_filePath, json, ct);
        return true;
    }

    private sealed class RunState
    {
        public DateTimeOffset? LastPublishedAt { get; set; }
    }
}
