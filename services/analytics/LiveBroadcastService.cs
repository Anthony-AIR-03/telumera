using Microsoft.AspNetCore.SignalR;

namespace Telumera.Services.Analytics.Api;

/// <summary>
/// Pushes a fresh <see cref="LiveSnapshot"/> to each watched site's SignalR group every
/// <c>Live:BroadcastIntervalSeconds</c> (default 5s). Same <see cref="BackgroundService"/> +
/// <see cref="PeriodicTimer"/> shape as <see cref="AnalyticsAggregationService"/> and
/// event-collector's <c>SiteProjectionSyncService</c> — recompute-and-push on a timer, not a
/// per-event fan-out, so a burst of traffic can't turn into a broadcast storm.
/// </summary>
public sealed class LiveBroadcastService(
    LiveVisitorProjection projection,
    LiveSubscriptions subscriptions,
    IHubContext<LiveHub> hub,
    IConfiguration configuration,
    ILogger<LiveBroadcastService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = int.TryParse(configuration["Live:BroadcastIntervalSeconds"], out var s) && s > 0 ? s : 5;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(interval));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                foreach (var siteId in subscriptions.ActiveSites)
                {
                    var snapshot = await projection.GetSnapshotAsync(siteId);
                    await hub.Clients.Group(LiveGroups.For(siteId)).SendAsync("live", snapshot, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Live broadcast tick failed; will retry next interval.");
            }
        }
    }
}
