using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.EntityFrameworkCore;

using Telumera.EventContracts;

namespace Telumera.Services.SiteRegistry.Api;

/// <summary>
/// Drains <see cref="OutboxEvent"/> rows and publishes them through the Dapr sidecar's HTTP API, per
/// docs/adr/0004-transactional-outbox-and-idempotent-consumers.md. Publishes as a fully-formed
/// CloudEvent (Content-Type: application/cloudevents+json) so Dapr uses Telumera's own
/// id/type/source/subject instead of auto-generating them, per ADR 0002's explicit envelope-ownership
/// rule. Polling (not LISTEN/NOTIFY) is enough at this volume — revisit if outbox latency ever matters.
/// </summary>
public sealed class OutboxPublisher(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<OutboxPublisher> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Dapr pub/sub topic all Site Registry events publish under (see
    /// services/site-registry/README.md for why one topic covers every event type this service
    /// emits, rather than one topic per event type).
    /// </summary>
    private const string Topic = "site-events";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var daprHttpPort = configuration["DAPR_HTTP_PORT"] ?? "3500";
        var publishUrl = $"http://localhost:{daprHttpPort}/v1.0/publish/pubsub/{Topic}";

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PublishPendingAsync(publishUrl, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Outbox publish pass failed; will retry next poll.");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private async Task PublishPendingAsync(string publishUrl, CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SiteRegistryDbContext>();

        var pending = await db.OutboxEvents
            .Where(e => e.PublishedAt == null)
            .OrderBy(e => e.CreatedAt)
            .Take(20)
            .ToListAsync(stoppingToken);

        if (pending.Count == 0)
        {
            return;
        }

        var httpClient = httpClientFactory.CreateClient(nameof(OutboxPublisher));

        foreach (var outboxEvent in pending)
        {
            var cloudEvent = BuildCloudEvent(outboxEvent);

            using var content = JsonContent.Create(cloudEvent);
            content.Headers.ContentType = new("application/cloudevents+json");

            using var response = await httpClient.PostAsync(publishUrl, content, stoppingToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Publish failed for outbox event {EventId} ({EventType}): {StatusCode}. Will retry next poll.",
                    outboxEvent.Id, outboxEvent.EventType, response.StatusCode);
                continue;
            }

            outboxEvent.PublishedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(stoppingToken);
    }

    private static JsonObject BuildCloudEvent(OutboxEvent outboxEvent)
    {
        var envelope = new EventEnvelope<JsonNode?>(
            TenantId: outboxEvent.WorkspaceId.ToString(),
            SiteId: outboxEvent.SiteId.ToString(),
            CorrelationId: outboxEvent.CorrelationId.ToString(),
            DataVersion: 1,
            Data: JsonNode.Parse(outboxEvent.DataJson));

        return new JsonObject
        {
            ["specversion"] = "1.0",
            ["id"] = outboxEvent.Id.ToString(),
            ["type"] = outboxEvent.EventType,
            ["source"] = "telumera.site-registry",
            ["subject"] = $"sites/{outboxEvent.SiteId}",
            ["time"] = outboxEvent.CreatedAt.ToString("O"),
            ["datacontenttype"] = "application/json",
            ["data"] = JsonSerializer.SerializeToNode(envelope),
        };
    }
}
