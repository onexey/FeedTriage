using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RssSummarizer.Worker.Configuration;
using RssSummarizer.Worker.Interfaces;

namespace RssSummarizer.Worker.Services;

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
        if (!File.Exists(_filePath))
        {
            _logger.LogInformation(
                "No state file found at {Path} — starting from scratch (all unread entries)", _filePath);
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(_filePath, ct);
            var state = JsonSerializer.Deserialize<RunState>(json, JsonOptions);
            _logger.LogInformation(
                "Loaded run state from {Path} — last published at: {PublishedAt}",
                _filePath, state?.LastPublishedAt);
            return state?.LastPublishedAt;
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
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var state = new RunState { LastPublishedAt = publishedAt };
            var json = JsonSerializer.Serialize(state, JsonOptions);
            await File.WriteAllTextAsync(_filePath, json, ct);
            _logger.LogDebug(
                "Saved run state to {Path} — last published at: {PublishedAt}", _filePath, publishedAt);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to save state file at {Path}. " +
                "If running in Docker, mount a writable volume and set RSSSUMMARIZER__STATE__FILE_PATH " +
                "to a path inside it (e.g. /data/state.json).",
                _filePath);
        }
    }

    private sealed class RunState
    {
        public DateTimeOffset? LastPublishedAt { get; set; }
    }
}
