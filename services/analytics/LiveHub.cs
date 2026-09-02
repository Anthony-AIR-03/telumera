using System.Collections.Concurrent;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Telumera.Services.Analytics.Api;

/// <summary>SignalR group name for one site's live subscribers.</summary>
public static class LiveGroups
{
    public static string For(Guid siteId) => $"site:{siteId:N}";
}

/// <summary>
/// Which sites currently have at least one connected live subscriber, so
/// <see cref="LiveBroadcastService"/> only computes and pushes snapshots for sites someone is
/// actually watching (there is no point recomputing a Redis projection nobody is looking at).
/// </summary>
public sealed class LiveSubscriptions
{
    private readonly ConcurrentDictionary<Guid, int> _counts = new();

    public void Add(Guid siteId) => _counts.AddOrUpdate(siteId, 1, (_, current) => current + 1);

    public void Remove(Guid siteId) => _counts.AddOrUpdate(siteId, 0, (_, current) => Math.Max(0, current - 1));

    public IReadOnlyCollection<Guid> ActiveSites =>
        _counts.Where(kv => kv.Value > 0).Select(kv => kv.Key).ToArray();
}

/// <summary>
/// The dashboard's live-visitors panel connects here directly (not through the gateway — a Dapr
/// service-invocation forwarder can't carry a WebSocket). Every connection carries the same Entra
/// bearer the REST API uses (SignalR sends it as the <c>access_token</c> query param on the WS
/// handshake — see Program.cs's JwtBearer wire-up), and <see cref="Subscribe"/> runs the exact same
/// <see cref="QueryAuthorization.AuthorizeSiteReadAsync"/> membership check the query endpoints use.
/// </summary>
[Authorize("ApiScope")]
public sealed class LiveHub(
    SiteLookupClient siteLookupClient,
    MembershipClient membershipClient,
    LiveSubscriptions subscriptions) : Hub
{
    // connectionId -> the sites it joined. SignalR serializes hub invocations per connection
    // (MaximumParallelInvocationsPerClient = 1 by default), so the HashSet needs no lock.
    private static readonly ConcurrentDictionary<string, HashSet<Guid>> ConnectionSites = new();

    public async Task Subscribe(string siteId)
    {
        if (!Guid.TryParse(siteId, out var id))
        {
            throw new HubException("Invalid siteId.");
        }

        var http = Context.GetHttpContext() ?? throw new HubException("No HTTP context on this connection.");
        var auth = await QueryAuthorization.AuthorizeSiteReadAsync(
            http, id, siteLookupClient, membershipClient, Context.ConnectionAborted);
        if (auth.Error is not null)
        {
            throw new HubException("Not authorized for this site.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, LiveGroups.For(id));
        subscriptions.Add(id);
        ConnectionSites.GetOrAdd(Context.ConnectionId, static _ => []).Add(id);
    }

    public async Task Unsubscribe(string siteId)
    {
        if (!Guid.TryParse(siteId, out var id))
        {
            return;
        }

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, LiveGroups.For(id));
        subscriptions.Remove(id);
        if (ConnectionSites.TryGetValue(Context.ConnectionId, out var joined))
        {
            joined.Remove(id);
        }
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        if (ConnectionSites.TryRemove(Context.ConnectionId, out var joined))
        {
            foreach (var id in joined)
            {
                subscriptions.Remove(id);
            }
        }

        return base.OnDisconnectedAsync(exception);
    }
}
