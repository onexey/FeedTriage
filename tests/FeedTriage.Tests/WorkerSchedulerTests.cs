using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using FeedTriage.Worker.Configuration;
using FeedTriage.Worker.Interfaces;
using FeedTriage.Worker.Models;
using Xunit;
using WorkerService = FeedTriage.Worker.Worker;

namespace FeedTriage.Tests;

public sealed class WorkerSchedulerTests
{
    private static WorkerService CreateWorker(
        IArticleProcessor processor,
        bool runOnStart = true,
        TimeSpan? interval = null)
    {
        var opts = Options.Create(new SchedulerOptions
        {
            RunOnStart = runOnStart,
            RunInterval = interval ?? TimeSpan.FromMilliseconds(50)
        });
        return new WorkerService(processor, opts, NullLogger<WorkerService>.Instance);
    }

    [Fact]
    public async Task Worker_RunsImmediately_WhenRunOnStartIsTrue()
    {
        var processorMock = new Mock<IArticleProcessor>();
        processorMock.Setup(p => p.ProcessAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RunSummary { StartedAt = DateTimeOffset.UtcNow });

        var worker = CreateWorker(processorMock.Object, runOnStart: true, interval: TimeSpan.FromHours(1));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        // Cancel after a brief moment so the worker doesn't loop
        _ = Task.Delay(200).ContinueWith(_ => cts.Cancel());

        await worker.StartAsync(cts.Token);
        await Task.Delay(300); // allow the run to complete
        await worker.StopAsync(default);

        processorMock.Verify(p => p.ProcessAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task Worker_DoesNotRunImmediately_WhenRunOnStartIsFalse()
    {
        var processorMock = new Mock<IArticleProcessor>();
        processorMock.Setup(p => p.ProcessAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RunSummary { StartedAt = DateTimeOffset.UtcNow });

        var worker = CreateWorker(processorMock.Object, runOnStart: false, interval: TimeSpan.FromHours(24));

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        await worker.StartAsync(cts.Token);
        await Task.Delay(400);
        await worker.StopAsync(default);

        // With a 24h interval and runOnStart=false, ProcessAsync should never be called
        processorMock.Verify(p => p.ProcessAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Worker_RepeatsOnInterval()
    {
        var callCount = 0;
        var processorMock = new Mock<IArticleProcessor>();
        processorMock.Setup(p => p.ProcessAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return new RunSummary { StartedAt = DateTimeOffset.UtcNow };
            });

        // Short interval so we get multiple runs
        var worker = CreateWorker(processorMock.Object, runOnStart: true, interval: TimeSpan.FromMilliseconds(80));

        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);
        await Task.Delay(350); // enough for ~3-4 runs
        cts.Cancel();
        await worker.StopAsync(default);

        Assert.True(callCount >= 2, $"Expected at least 2 runs but got {callCount}");
    }

    [Fact]
    public async Task Worker_AcceptsIntervalsLongerThanTaskDelayLimit()
    {
        var processorMock = new Mock<IArticleProcessor>();
        processorMock.Setup(p => p.ProcessAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RunSummary { StartedAt = DateTimeOffset.UtcNow });

        var worker = CreateWorker(
            processorMock.Object,
            runOnStart: true,
            interval: TimeSpan.FromDays(365));

        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);
        await Task.Delay(150);
        await worker.StopAsync(default);

        processorMock.Verify(p => p.ProcessAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
