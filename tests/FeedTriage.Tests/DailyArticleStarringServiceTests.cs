using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using FeedTriage.Worker.Interfaces;
using FeedTriage.Worker.Models;
using FeedTriage.Worker.Services;
using Xunit;

namespace FeedTriage.Tests;

public sealed class DailyArticleStarringServiceTests
{
    [Fact]
    public async Task RunPendingAsync_StarsTopFiveForEachPendingDay_AndUpdatesLastRun()
    {
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        var scoreRepository = new Mock<IArticleScoreRepository>();
        scoreRepository.Setup(r => r.GetLastDailyStarringRunAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((DateTimeOffset?)null);
        scoreRepository.Setup(r => r.GetScoredDatesAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([yesterday]);
        scoreRepository.Setup(r => r.GetTopScoresAsync(yesterday, 5, 6, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Range(1, 6)
                .Select(i => new StoredArticleScore
                {
                    ScoreDate = yesterday,
                    EntryId = i,
                    Title = $"Article {i}",
                    Url = $"https://example.com/{i}",
                    TotalScore = 10 - i,
                    TopicScores = new Dictionary<string, int> { ["software engineering"] = 5 }
                })
                .Take(5)
                .ToList());
        scoreRepository.Setup(r => r.SaveLastDailyStarringRunAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        scoreRepository.Setup(r => r.DeleteScoresOlderThanAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var miniflux = new Mock<IMinifluxClient>();
        miniflux.Setup(m => m.BookmarkAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = new DailyArticleStarringService(
            scoreRepository.Object,
            miniflux.Object,
            NullLogger<DailyArticleStarringService>.Instance);

        await service.RunPendingAsync();

        miniflux.Verify(m => m.BookmarkAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Exactly(5));
        scoreRepository.Verify(r => r.SaveLastDailyStarringRunAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
        scoreRepository.Verify(r => r.DeleteScoresOlderThanAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
