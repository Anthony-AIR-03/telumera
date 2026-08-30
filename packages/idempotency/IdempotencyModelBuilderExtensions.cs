using Microsoft.EntityFrameworkCore;

namespace Telumera.Idempotency;

public static class IdempotencyModelBuilderExtensions
{
    /// <summary>
    /// Maps <see cref="ProcessedEvent"/> to the <c>processed_events</c> table. Each consuming service
    /// calls this from its own <c>OnModelCreating</c> and owns its own table — per
    /// docs/adr/0005-context-level-data-isolation.md, there is no shared idempotency store, only a
    /// shared mapping (same split as packages/outbox's <c>ConfigureOutboxEvent</c>).
    /// </summary>
    public static ModelBuilder ConfigureProcessedEvent(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProcessedEvent>(entity =>
        {
            entity.ToTable("processed_events");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.EventType).IsRequired().HasMaxLength(200);
        });

        return modelBuilder;
    }
}
