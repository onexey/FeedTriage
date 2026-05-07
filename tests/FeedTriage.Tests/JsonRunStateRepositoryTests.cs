using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using FeedTriage.Worker.Configuration;
using FeedTriage.Worker.Services;
using Xunit;

namespace FeedTriage.Tests;

public sealed class JsonRunStateRepositoryTests
{
    [Fact]
    public async Task GetLastPublishedAtAsync_CreatesStateFile_WhenMissing()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"feedtriage-state-{Guid.NewGuid():N}");
        var filePath = Path.Combine(tempRoot, "state.json");

        try
        {
            var repository = CreateRepository(filePath);

            var publishedAt = await repository.GetLastPublishedAtAsync();

            Assert.Null(publishedAt);
            Assert.True(File.Exists(filePath));
            await AssertFileContainsAsync(filePath, "\"lastPublishedAt\": null");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SaveLastPublishedAtAsync_WritesStateFile_WhenMissing()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"feedtriage-state-{Guid.NewGuid():N}");
        var filePath = Path.Combine(tempRoot, "state.json");
        var expected = new DateTimeOffset(2026, 05, 07, 12, 30, 00, TimeSpan.Zero);

        try
        {
            var repository = CreateRepository(filePath);

            await repository.SaveLastPublishedAtAsync(expected);

            Assert.True(File.Exists(filePath));
            var actual = await repository.GetLastPublishedAtAsync();
            Assert.Equal(expected, actual);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static JsonRunStateRepository CreateRepository(string filePath)
    {
        var options = Options.Create(new StateOptions { FilePath = filePath });
        return new JsonRunStateRepository(options, NullLogger<JsonRunStateRepository>.Instance);
    }

    private static async Task AssertFileContainsAsync(string filePath, string expectedText)
    {
        var content = await File.ReadAllTextAsync(filePath);
        Assert.Contains(expectedText, content);
    }
}
