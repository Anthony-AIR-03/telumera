using Microsoft.EntityFrameworkCore;

namespace Telumera.Services.Analytics.Api;

/// <summary>
/// Persists <see cref="AnalyticsAggregationService"/>'s watermark across restarts — the high-water mark
/// of `events.received_at` already scanned for session recomputation, keyed by job name so a second,
/// unrelated periodic job (none exists yet) could add its own row without colliding. Lives in
/// `telumera_analytics_control` alongside packages/idempotency's `processed_events`, same "small
/// Postgres control table next to the real ClickHouse data" pattern.
/// </summary>
public sealed class AggregationCheckpoint
{
    public required string JobName { get; init; }

    /// <summary>Unset (never run) is represented by <see cref="DateTimeOffset.MinValue"/>, not a nullable column — every events row's received_at is always later than this, so the first-ever run naturally scans/back-fills everything.</summary>
    public DateTimeOffset Watermark { get; set; } = DateTimeOffset.MinValue;
}

public static class AggregationCheckpointModelBuilderExtensions
{
    public static ModelBuilder ConfigureAggregationCheckpoint(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AggregationCheckpoint>(entity =>
        {
            entity.ToTable("aggregation_checkpoints");
            entity.HasKey(e => e.JobName);
            entity.Property(e => e.JobName).IsRequired().HasMaxLength(100);
        });

        return modelBuilder;
    }
}
