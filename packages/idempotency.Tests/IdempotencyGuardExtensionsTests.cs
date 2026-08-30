using Microsoft.EntityFrameworkCore;

namespace Telumera.Idempotency.Tests;

/// <summary>
/// Proves the dedup contract packages/idempotency/README.md documents, against an in-memory DbContext
/// rather than a real subscriber — see that README for why (no real consumer exists in the codebase yet).
/// </summary>
public sealed class IdempotencyGuardExtensionsTests
{
    private static TestDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task TryBeginProcessingEventAsync_FirstTimeSeeingEvent_ReturnsTrue()
    {
        await using var db = CreateDbContext();
        var eventId = Guid.NewGuid();

        var isNewEvent = await db.TryBeginProcessingEventAsync(eventId, "site.created.v1");

        Assert.True(isNewEvent);
    }

    [Fact]
    public async Task TryBeginProcessingEventAsync_SavedAndRedelivered_ReturnsFalseOnSecondCall()
    {
        await using var db = CreateDbContext();
        var eventId = Guid.NewGuid();

        await db.TryBeginProcessingEventAsync(eventId, "site.created.v1");
        await db.SaveChangesAsync();

        var isNewEvent = await db.TryBeginProcessingEventAsync(eventId, "site.created.v1");

        Assert.False(isNewEvent);
    }

    [Fact]
    public async Task TryBeginProcessingEventAsync_WithoutSaveChanges_MarkerDoesNotPersist()
    {
        // Simulates a handler that begins processing but crashes/throws before SaveChangesAsync —
        // the marker must not have committed, so redelivery is treated as a first attempt, not a
        // false "already processed".
        var eventId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString();

        await using (var db = new TestDbContext(new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(databaseName).Options))
        {
            await db.TryBeginProcessingEventAsync(eventId, "site.created.v1");
            // Deliberately no SaveChangesAsync() — the "crash before commit" case.
        }

        await using (var db = new TestDbContext(new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(databaseName).Options))
        {
            var isNewEvent = await db.TryBeginProcessingEventAsync(eventId, "site.created.v1");

            Assert.True(isNewEvent);
        }
    }

    [Fact]
    public async Task TryBeginProcessingEventAsync_DifferentEventIds_AreTrackedIndependently()
    {
        await using var db = CreateDbContext();
        var firstEventId = Guid.NewGuid();
        var secondEventId = Guid.NewGuid();

        await db.TryBeginProcessingEventAsync(firstEventId, "site.created.v1");
        await db.SaveChangesAsync();

        var isSecondEventNew = await db.TryBeginProcessingEventAsync(secondEventId, "site.created.v1");

        Assert.True(isSecondEventNew);
    }
}
