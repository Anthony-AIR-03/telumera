namespace WorkerServiceTemplate;

/// <summary>
/// Tracks which at-least-once event ids have already been processed so retried or duplicate
/// deliveries (see ADR 0002) are safely skipped rather than double-processed. Replace
/// <see cref="InMemoryIdempotencyStore"/> with a persistent store (e.g. a table in the service's own
/// database) before this worker consumes real events — an in-memory store forgets everything on
/// restart, which defeats the purpose once the process actually crashes and retries.
/// </summary>
public interface IIdempotencyStore
{
    Task<bool> IsProcessedAsync(string messageId, CancellationToken cancellationToken);

    Task MarkProcessedAsync(string messageId, CancellationToken cancellationToken);
}
