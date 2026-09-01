using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;

using Telumera.EventContracts;

namespace Telumera.Services.EventCollector.Api;

public sealed record EnrichedEvent(
    string Id,
    string EventType,
    Guid SiteId,
    Guid WorkspaceId,
    string CorrelationId,
    DateTimeOffset ReceivedAt,
    object Data);

/// <summary>
/// Bounded producer/consumer buffer between the ingestion endpoint and <see cref="CollectorPublisher"/>
/// — the confirmed design decision for this epic: the endpoint enqueues and returns 202 immediately
/// rather than waiting on a synchronous Dapr publish call, decoupling client-facing latency from
/// publish latency. Per ADR 0004, this service is explicitly exempted from the transactional-outbox
/// pattern used elsewhere, "accepting some risk of an unpublished sample" on a crash between accept
/// and publish — DropOldest keeps ingestion itself from ever blocking under sustained overload,
/// trading the oldest buffered events for headroom rather than applying backpressure to /v1/events.
/// </summary>
public sealed class CollectorChannel
{
    private readonly Channel<EnrichedEvent> _channel = Channel.CreateBounded<EnrichedEvent>(
        new BoundedChannelOptions(10_000) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    public ChannelReader<EnrichedEvent> Reader => _channel.Reader;

    public bool TryEnqueue(EnrichedEvent enrichedEvent) => _channel.Writer.TryWrite(enrichedEvent);
}

/// <summary>
/// Drains <see cref="CollectorChannel"/> and publishes each event as a CloudEvent to Dapr pub/sub, same
/// URL/content-type shape as packages/outbox/OutboxPublisher.cs uses, on a single topic
/// ("collector-events") rather than one per event type — matching the "one topic per publishing
/// service" convention site-registry already established with "site-events" (downstream services
/// subscribe to this topic and filter by CloudEvent `type`).
/// </summary>
public sealed class CollectorPublisher(
    CollectorChannel channel,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<CollectorPublisher> logger) : BackgroundService
{
    private const string Topic = "collector-events";
    private const string Source = "telumera.event-collector";
    private const string HttpClientName = "Telumera.EventCollector.Publisher";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var daprHttpPort = configuration["DAPR_HTTP_PORT"] ?? "3500";
        var publishUrl = $"http://localhost:{daprHttpPort}/v1.0/publish/pubsub/{Topic}";
        var httpClient = httpClientFactory.CreateClient(HttpClientName);

        await foreach (var enrichedEvent in channel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                var cloudEvent = BuildCloudEvent(enrichedEvent);
                using var content = JsonContent.Create(cloudEvent);
                content.Headers.ContentType = new("application/cloudevents+json");

                using var response = await httpClient.PostAsync(publishUrl, content, stoppingToken);
                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning(
                        "Publish failed for event {EventId} ({EventType}): {StatusCode}. No retry — dropped (ADR 0004).",
                        enrichedEvent.Id, enrichedEvent.EventType, response.StatusCode);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex,
                    "Publish threw for event {EventId} ({EventType}). No retry — dropped (ADR 0004).",
                    enrichedEvent.Id, enrichedEvent.EventType);
            }
        }
    }

    private static JsonObject BuildCloudEvent(EnrichedEvent enrichedEvent)
    {
        var envelope = new EventEnvelope<object>(
            TenantId: enrichedEvent.WorkspaceId.ToString(),
            SiteId: enrichedEvent.SiteId.ToString(),
            CorrelationId: enrichedEvent.CorrelationId,
            DataVersion: 1,
            Data: enrichedEvent.Data);

        return new JsonObject
        {
            ["specversion"] = "1.0",
            ["id"] = enrichedEvent.Id,
            ["type"] = enrichedEvent.EventType,
            ["source"] = Source,
            ["subject"] = $"sites/{enrichedEvent.SiteId}",
            ["time"] = enrichedEvent.ReceivedAt.ToString("O"),
            ["datacontenttype"] = "application/json",
            ["data"] = JsonSerializer.SerializeToNode(envelope),
        };
    }
}
