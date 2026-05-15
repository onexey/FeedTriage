using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FeedTriage.Worker.Configuration;
using FeedTriage.Worker.Interfaces;

namespace FeedTriage.Worker;

public sealed class Worker : BackgroundService
{
    private static readonly TimeSpan MaxDelay = TimeSpan.FromMilliseconds(int.MaxValue - 1);

    private readonly IArticleProcessor _processor;
    private readonly IDailyArticleStarringService _dailyArticleStarring;
    private readonly SchedulerOptions _scheduler;
    private readonly ILogger<Worker> _logger;

    public Worker(
        IArticleProcessor processor,
        IDailyArticleStarringService dailyArticleStarring,
        IOptions<SchedulerOptions> schedulerOptions,
        ILogger<Worker> logger)
    {
        _processor = processor;
        _dailyArticleStarring = dailyArticleStarring;
        _scheduler = schedulerOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Worker started — RunOnStart={RunOnStart}, RunInterval={RunInterval}",
            _scheduler.RunOnStart, _scheduler.RunInterval);

        await RunDailyStarringAsync(stoppingToken);

        if (_scheduler.RunOnStart)
        {
            await RunOnceAsync(stoppingToken);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DelayUntilNextRunAsync(_scheduler.RunInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (!stoppingToken.IsCancellationRequested)
                await RunOnceAsync(stoppingToken);
        }

        _logger.LogInformation("Worker stopping");
    }

    private static async Task DelayUntilNextRunAsync(TimeSpan runInterval, CancellationToken cancellationToken)
    {
        var remainingDelay = runInterval;

        while (remainingDelay > TimeSpan.Zero)
        {
            var currentDelay = remainingDelay > MaxDelay ? MaxDelay : remainingDelay;
            await Task.Delay(currentDelay, cancellationToken);
            remainingDelay -= currentDelay;
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        _logger.LogInformation("--- Starting processing run at {Time} ---", DateTimeOffset.UtcNow);
        try
        {
            await _processor.ProcessAsync(ct);
            await RunDailyStarringAsync(ct);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Processing run cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error during processing run");
        }
    }

    private async Task RunDailyStarringAsync(CancellationToken ct)
    {
        try
        {
            await _dailyArticleStarring.RunPendingAsync(ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error during daily starring run");
        }
    }
}
