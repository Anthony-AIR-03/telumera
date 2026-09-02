using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

using Telumera.EventContracts;

namespace Telumera.Services.EventCollector.Api;

/// <summary>Payload of <c>collector.quality.v1</c> — a batch of per-(site, dimension) count deltas
/// accumulated since the previous publish. Additive contract, <c>dataVersion: 1</c>; every consumer
/// must be idempotent (the analytics side dedups on the CloudEvent id before applying the deltas).</summary>
public sealed record CollectorQualityPayload(string Date, IReadOnlyList<CollectorQualityCount> Counts);

public sealed record CollectorQualityCount(string SiteId, string Dimension, long Count);

/// <summary>
/// Drains <see cref="QualityCounters"/> every <c>Quality:PublishIntervalSeconds</c> (default 60) and
/// publishes the deltas to the <c>quality-events</c> Dapr topic — same publish shape as
/// <see cref="CollectorPublisher"/>. Deltas are restored to the counter on a failed publish so the
/// next interval retries them.
/// </summary>
public sealed class QualityRollupPublisher(
    QualityCounters counters,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<QualityRollupPublisher> logger) : BackgroundService
{
    private const string Topic = "quality-events";
    private const string Source = "telumera.event-collector";
    private const string HttpClientName = "Telumera.EventCollector.QualityPublisher";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalSeconds = int.TryParse(configuration["Quality:PublishIntervalSeconds"], out var s) && s > 0 ? s : 60;
        var daprHttpPort = configuration["DAPR_HTTP_PORT"] ?? "3500";
        var publishUrl = $"http://localhost:{daprHttpPort}/v1.0/publish/pubsub/{Topic}";
        var httpClient = httpClientFactory.CreateClient(HttpClientName);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(intervalSeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var drained = counters.Drain();
            if (drained.Count == 0)
            {
                continue;
            }

            try
            {
                var payload = new CollectorQualityPayload(
                    DateTime.UtcNow.ToString("yyyy-MM-dd"),
                    drained.Select(d => new CollectorQualityCount(d.SiteId.ToString(), d.Dimension, d.Count)).ToArray());

                var cloudEvent = new JsonObject
                {
                    ["specversion"] = "1.0",
                    ["id"] = Guid.NewGuid().ToString(),
                    ["type"] = EventTypes.CollectorQualityV1,
                    ["source"] = Source,
                    ["time"] = DateTimeOffset.UtcNow.ToString("O"),
                    ["datacontenttype"] = "application/json",
                    ["data"] = JsonSerializer.SerializeToNode(payload),
                };

                using var content = JsonContent.Create(cloudEvent);
                content.Headers.ContentType = new("application/cloudevents+json");
                using var response = await httpClient.PostAsync(publishUrl, content, stoppingToken);
                if (!response.IsSuccessStatusCode)
                {
                    counters.Restore(drained);
                    logger.LogWarning(
                        "Publishing collector.quality.v1 failed: {StatusCode}. Deltas restored for next interval.",
                        response.StatusCode);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                counters.Restore(drained);
                break;
            }
            catch (Exception ex)
            {
                counters.Restore(drained);
                logger.LogError(ex, "Quality rollup publish threw; deltas restored for next interval.");
            }
        }
    }
}
