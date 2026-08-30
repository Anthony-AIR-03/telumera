using Microsoft.EntityFrameworkCore;

namespace Telumera.Outbox;

public static class OutboxModelBuilderExtensions
{
    /// <summary>
    /// Maps <see cref="OutboxEvent"/> to the <c>outbox_events</c> table. Each service still calls this
    /// from its own <c>OnModelCreating</c> — per docs/adr/0005-context-level-data-isolation.md, every
    /// context owns its own schema/database, so there is no shared outbox table, only a shared mapping.
    /// </summary>
    public static ModelBuilder ConfigureOutboxEvent(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxEvent>(entity =>
        {
            entity.ToTable("outbox_events");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.EventType).IsRequired().HasMaxLength(200);
            entity.Property(e => e.CorrelationId).IsRequired().HasMaxLength(64);
            entity.Property(e => e.DataJson).IsRequired().HasColumnType("jsonb");
            entity.HasIndex(e => e.PublishedAt);
        });

        return modelBuilder;
    }
}
