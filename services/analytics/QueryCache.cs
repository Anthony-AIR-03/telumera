using System.Text.Json;

using Microsoft.Extensions.Caching.Distributed;

namespace Telumera.Services.Analytics.Api;

/// <summary>
/// M01.6's cache half of "Add query cache and timeout limits" — the first real Redis consumer in this
/// repo (infrastructure/compose/docker-compose.yml's `redis` container has run unused since M00.3).
/// Uses ASP.NET Core's standard IDistributedCache/AddStackExchangeRedisCache rather than a hand-rolled
/// HTTP client — unlike ClickHouseWriter's raw-HTTP approach (justified there by ClickHouse.Client
/// having no confirmed net10.0 target), Redis's official client has no such gap, so there's no reason
/// to avoid the idiomatic abstraction.
/// </summary>
public sealed class QueryCache(IDistributedCache cache)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<T> GetOrSetAsync<T>(string key, TimeSpan ttl, Func<Task<T>> factory, CancellationToken cancellationToken)
    {
        var cached = await cache.GetStringAsync(key, cancellationToken);
        if (cached is not null)
        {
            var deserialized = JsonSerializer.Deserialize<T>(cached, JsonOptions);
            if (deserialized is not null)
            {
                return deserialized;
            }
        }

        var value = await factory();
        await cache.SetStringAsync(
            key, JsonSerializer.Serialize(value, JsonOptions),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl },
            cancellationToken);
        return value;
    }
}
