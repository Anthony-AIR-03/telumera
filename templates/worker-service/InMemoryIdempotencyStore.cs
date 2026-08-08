using System.Collections.Concurrent;

namespace WorkerServiceTemplate;

/// <summary>
/// Development-only default. Not durable across restarts — see <see cref="IIdempotencyStore"/>.
/// </summary>
public sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    private readonly ConcurrentDictionary<string, byte> _processedMessageIds = new();

    public Task<bool> IsProcessedAsync(string messageId, CancellationToken cancellationToken)
        => Task.FromResult(_processedMessageIds.ContainsKey(messageId));

    public Task MarkProcessedAsync(string messageId, CancellationToken cancellationToken)
    {
        _processedMessageIds[messageId] = 0;
        return Task.CompletedTask;
    }
}
