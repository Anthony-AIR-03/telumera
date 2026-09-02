using Microsoft.EntityFrameworkCore;

namespace Telumera.Services.Analytics.Api;

/// <summary>
/// M01.5's periodic aggregation pass: recomputes `sessions` for anything dirty since the last watermark,
/// then recomputes the five daily rollups for exactly the (site, date) buckets those sessions touched —
/// see ClickHouseWriter.RecomputeSessionsAsync/RecomputeDailyRollupsAsync for the actual queries and why
/// this is periodic recomputation rather than a streaming materialized view (a 30-minute inactivity gap
/// can't be resolved by an insert-triggered view). Same `BackgroundService` + `PeriodicTimer` shape as
/// services/event-collector/SiteProjectionSyncService.cs, but needs `IServiceScopeFactory` (unlike that
/// example's singleton dependency) since `AnalyticsDbContext` is scoped.
/// </summary>
public sealed class AnalyticsAggregationService(
    IServiceScopeFactory scopeFactory,
    ClickHouseWriter clickHouse,
    IConfiguration configuration,
    ILogger<AnalyticsAggregationService> logger) : BackgroundService
{
    private const string JobName = "session-and-rollup-aggregation";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalSeconds = configuration.GetValue("Aggregation:IntervalSeconds", 60);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(intervalSeconds));

        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Same "log and retry next tick" resilience as packages/outbox's OutboxPublisher — a
                // transient ClickHouse/Postgres failure shouldn't crash the whole service, the next
                // tick picks up from the last successfully-committed watermark.
                logger.LogError(ex, "Analytics aggregation tick failed; will retry next interval.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnalyticsDbContext>();

        var checkpoint = await db.AggregationCheckpoints.FindAsync([JobName], cancellationToken);
        var isNewCheckpoint = checkpoint is null;
        checkpoint ??= new AggregationCheckpoint { JobName = JobName };
        var watermark = checkpoint.Watermark;

        var runStartedAt = DateTimeOffset.UtcNow;

        await clickHouse.RecomputeSessionsAsync(watermark, runStartedAt, cancellationToken);
        await clickHouse.RecomputeDailyRollupsAsync(runStartedAt, cancellationToken);

        // Trails "now" by a safety buffer rather than advancing straight to runStartedAt — an event can
        // land in ClickHouse noticeably after its own received_at (Dapr redelivery/retry per ADR 0004,
        // or ordinary publish-to-process lag); this bounds how far behind the watermark can safely sit
        // without skipping such a straggler on a later tick. Cheap to keep: RecomputeSessionsAsync only
        // re-derives DISTINCT session_id from the rescanned window, not a full re-aggregation of
        // everything in it.
        var safetyBufferSeconds = configuration.GetValue("Aggregation:WatermarkSafetyBufferSeconds", 300);
        checkpoint.Watermark = runStartedAt - TimeSpan.FromSeconds(safetyBufferSeconds);

        if (isNewCheckpoint)
        {
            db.AggregationCheckpoints.Add(checkpoint);
        }
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Analytics aggregation tick complete: rescanned events since {PreviousWatermark}, watermark advanced to {NewWatermark}.",
            watermark, checkpoint.Watermark);
    }
}
