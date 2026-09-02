using StackExchange.Redis;

namespace Telumera.Services.Analytics.Api;

/// <summary>
/// Point-in-time "who is on the site right now" — a rolling <c>Live:WindowSeconds</c> (default 300s)
/// window kept entirely in Redis. Per <c>planning/Telumera_Modular_Project_Plan.md</c> §9 and
/// <c>docs/analytics/definitions-and-privacy-model.md</c> §9 this is explicitly <b>not durable
/// truth</b>: it is never persisted, never feeds a historical metric, carries no cross-day linkage,
/// and a lost Redis is a cosmetic outage rather than data loss. Bot traffic is excluded upstream
/// (<see cref="EventProcessor"/> only records non-bot events).
/// </summary>
public sealed class LiveVisitorProjection(IConnectionMultiplexer redis, IConfiguration configuration)
{
    private const int MaxPages = 8;

    private readonly IDatabase _db = redis.GetDatabase();

    private readonly int _windowSeconds =
        int.TryParse(configuration["Live:WindowSeconds"], out var w) && w > 0 ? w : 300;

    private static string VisitorsKey(Guid siteId) => $"live:{siteId:N}:visitors";

    private static string PathsKey(Guid siteId) => $"live:{siteId:N}:paths";

    /// <summary>Records that <paramref name="visitorId"/> was last seen on <paramref name="path"/>.</summary>
    public async Task RecordAsync(Guid siteId, string visitorId, string path, DateTimeOffset seenAt)
    {
        var visitorsKey = VisitorsKey(siteId);
        var pathsKey = PathsKey(siteId);
        var score = seenAt.ToUnixTimeSeconds();
        // Let idle sites' keys evaporate on their own — no separate janitor needed.
        var expiry = TimeSpan.FromSeconds(_windowSeconds * 2);

        var tx = _db.CreateTransaction();
        _ = tx.SortedSetAddAsync(visitorsKey, visitorId, score);
        _ = tx.HashSetAsync(pathsKey, visitorId, path);
        _ = tx.KeyExpireAsync(visitorsKey, expiry);
        _ = tx.KeyExpireAsync(pathsKey, expiry);
        await tx.ExecuteAsync();
    }

    /// <summary>Trims anything older than the window, then returns the current count + top pages.</summary>
    public async Task<LiveSnapshot> GetSnapshotAsync(Guid siteId)
    {
        var visitorsKey = VisitorsKey(siteId);
        var pathsKey = PathsKey(siteId);
        var cutoff = DateTimeOffset.UtcNow.AddSeconds(-_windowSeconds).ToUnixTimeSeconds();

        await _db.SortedSetRemoveRangeByScoreAsync(visitorsKey, double.NegativeInfinity, cutoff, Exclude.Stop);

        var members = await _db.SortedSetRangeByScoreAsync(visitorsKey, cutoff, double.PositiveInfinity);
        if (members.Length == 0)
        {
            return new LiveSnapshot(0, []);
        }

        var storedPaths = await _db.HashGetAsync(pathsKey, members);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var value in storedPaths)
        {
            if (value.IsNullOrEmpty)
            {
                continue;
            }
            var path = value.ToString();
            counts[path] = counts.GetValueOrDefault(path) + 1;
        }

        var pages = counts
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Take(MaxPages)
            .Select(kv => new LivePage(kv.Key, kv.Value))
            .ToArray();

        return new LiveSnapshot(members.Length, pages);
    }
}

public sealed record LiveSnapshot(int ActiveVisitors, IReadOnlyList<LivePage> Pages);

public sealed record LivePage(string Path, int Visitors);
