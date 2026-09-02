using Microsoft.EntityFrameworkCore;

using Telumera.Idempotency;

namespace Telumera.Services.Analytics.Api;

/// <summary>
/// Holds only packages/idempotency's `processed_events` table — the actual analytics data lives in
/// ClickHouse (see ClickHouseWriter.cs), not here. A separate small PostgreSQL database exists purely
/// so the consumer-dedup guard (`TryBeginProcessingEventAsync`) has a transactional store to commit
/// its marker in, per ADR 0003's "a service using both PostgreSQL and ClickHouse" pattern.
/// </summary>
public sealed class AnalyticsDbContext(DbContextOptions<AnalyticsDbContext> options) : DbContext(options)
{
    public DbSet<AggregationCheckpoint> AggregationCheckpoints => Set<AggregationCheckpoint>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ConfigureProcessedEvent();
        modelBuilder.ConfigureAggregationCheckpoint();
    }
}
