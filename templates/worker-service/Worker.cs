using System.Diagnostics;
using Polly;
using Polly.Retry;

namespace WorkerServiceTemplate;

public class Worker(ILogger<Worker> logger, IIdempotencyStore idempotencyStore) : BackgroundService
{
    private static readonly ActivitySource ActivitySource = new("WorkerServiceTemplate");

    private readonly ResiliencePipeline _retryPipeline = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            BackoffType = DelayBackoffType.Exponential,
            Delay = TimeSpan.FromSeconds(1),
        })
        .Build();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessNextAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Shutdown was requested (SIGTERM / Ctrl+C) — exit the loop instead of logging an error.
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled error processing work item; will retry after the next delay.");
            }
        }

        logger.LogInformation("Worker stopping gracefully.");
    }

    // Delete this once the service processes real messages — it demonstrates the retry +
    // idempotency + tracing conventions every consumer of an at-least-once event stream should follow.
    private async Task ProcessNextAsync(CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("ProcessWorkItem");

        // Replace with the real event id once this template is wired to an actual Dapr subscription.
        const string messageId = "example-message-id";

        if (await idempotencyStore.IsProcessedAsync(messageId, cancellationToken))
        {
            logger.LogDebug("Skipping already-processed message {MessageId}.", messageId);
            return;
        }

        await _retryPipeline.ExecuteAsync(async ct =>
        {
            logger.LogInformation("Processing work item at {Time}.", DateTimeOffset.UtcNow);
            await Task.Delay(TimeSpan.FromMilliseconds(100), ct);
        }, cancellationToken);

        await idempotencyStore.MarkProcessedAsync(messageId, cancellationToken);
    }
}
