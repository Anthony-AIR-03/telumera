namespace Telumera.Services.EventCollector.Api;

/// <summary>
/// Drives <see cref="SiteProjection.WarmUpAsync"/>: retries quickly on startup (the app and its Dapr
/// sidecar start concurrently — confirmed live, the very first warm-up attempt commonly races ahead of
/// the sidecar's own HTTP port coming up and fails with a connection error), then keeps resyncing
/// periodically once warmed up — a cheap way to self-heal from a missed subscription event rather than
/// relying purely on site.created.v1/site.settings.changed.v1/site.key.rotated.v1 plus the cache-miss
/// fallback to stay eventually consistent forever.
/// </summary>
public sealed class SiteProjectionSyncService(SiteProjection projection) : BackgroundService
{
    private static readonly TimeSpan StartupRetryDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ResyncInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested && !await projection.WarmUpAsync(stoppingToken))
        {
            await Task.Delay(StartupRetryDelay, stoppingToken);
        }

        using var timer = new PeriodicTimer(ResyncInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await projection.WarmUpAsync(stoppingToken);
        }
    }
}
