using Microsoft.EntityFrameworkCore;

namespace Telumera.Services.SiteRegistry.Api;

public sealed class SiteRegistryDbContext(DbContextOptions<SiteRegistryDbContext> options) : DbContext(options)
{
    public DbSet<Site> Sites => Set<Site>();

    public DbSet<SiteToken> SiteTokens => Set<SiteToken>();

    public DbSet<SiteModuleSetting> SiteModuleSettings => Set<SiteModuleSetting>();

    public DbSet<OutboxEvent> OutboxEvents => Set<OutboxEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Site>(entity =>
        {
            entity.ToTable("sites");
            entity.HasKey(s => s.Id);
            entity.Property(s => s.Name).IsRequired().HasMaxLength(200);
            entity.Property(s => s.CanonicalDomain).IsRequired().HasMaxLength(253);
            entity.Property(s => s.Environment).IsRequired().HasMaxLength(50);
        });

        modelBuilder.Entity<SiteToken>(entity =>
        {
            entity.ToTable("site_tokens");
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Token).IsRequired().HasMaxLength(64);
            entity.HasIndex(t => t.Token).IsUnique();
            entity.HasIndex(t => t.SiteId);
        });

        modelBuilder.Entity<SiteModuleSetting>(entity =>
        {
            entity.ToTable("site_module_settings");
            entity.HasKey(s => s.Id);
            entity.HasIndex(s => new { s.SiteId, s.Module }).IsUnique();
        });

        modelBuilder.Entity<OutboxEvent>(entity =>
        {
            entity.ToTable("outbox_events");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.EventType).IsRequired().HasMaxLength(200);
            entity.Property(e => e.DataJson).IsRequired().HasColumnType("jsonb");
            entity.HasIndex(e => e.PublishedAt);
        });
    }
}
