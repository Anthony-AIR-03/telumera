using Microsoft.EntityFrameworkCore;

namespace Telumera.Idempotency.Tests;

/// <summary>Minimal stand-in for a real consumer's own DbContext — only maps ProcessedEvent.</summary>
internal sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ConfigureProcessedEvent();
    }
}
