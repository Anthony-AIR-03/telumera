using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Telumera.EventContracts;

namespace Telumera.Outbox;

/// <summary>
/// Drains <see cref="OutboxEvent"/> rows from <typeparamref name="TDbContext"/> and publishes them
/// through the local Dapr sidecar's HTTP API, per
/// docs/adr/0004-transactional-outbox-and-idempotent-consumers.md. Publishes as a fully-formed
/// CloudEvent (Content-Type: application/cloudevents+json) so Dapr uses Telumera's own
/// id/type/source/subject instead of auto-generating them, per ADR 0002's explicit envelope-ownership
/// rule. Polling (not LISTEN/NOTIFY) is enough at this volume — revisit if outbox latency ever matters.
/// One instance per service, registered via <see cref="OutboxServiceCollectionExtensions.AddOutboxPublisher{TDbContext}"/>.
/// </summary>
public sealed class OutboxPublisher<TDbContext>(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    IOptions<OutboxPublisherOptions> options,
    ILogger<OutboxPublisher<TDbContext>> logger) : BackgroundService
    where TDbContext : DbContext
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private const string HttpClientName = "Telumera.Outbox.OutboxPublisher";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var daprHttpPort = configuration["DAPR_HTTP_PORT"] ?? "3500";
        var publishUrl = $"http://localhost:{daprHttpPort}/v1.0/publish/pubsub/{options.Value.Topic}";

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
        var db = scope.ServiceProvider.GetRequiredService<TDbContext>();

        var pending = await db.Set<OutboxEvent>()
            .Where(e => e.PublishedAt == null)
            .OrderBy(e => e.CreatedAt)
            .Take(20)
            .ToListAsync(stoppingToken);

        if (pending.Count == 0)
        {
            return;
        }

        var httpClient = httpClientFactory.CreateClient(HttpClientName);

        foreach (var outboxEvent in pending)
        {
            var cloudEvent = BuildCloudEvent(outboxEvent, options.Value.Source);

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

    private static JsonObject BuildCloudEvent(OutboxEvent outboxEvent, string source)
    {
        var envelope = new EventEnvelope<JsonNode?>(
            TenantId: outboxEvent.TenantId.ToString(),
            SiteId: outboxEvent.SiteId.ToString(),
            CorrelationId: outboxEvent.CorrelationId.ToString(),
            DataVersion: 1,
            Data: JsonNode.Parse(outboxEvent.DataJson));

        return new JsonObject
        {
            ["specversion"] = "1.0",
            ["id"] = outboxEvent.Id.ToString(),
            ["type"] = outboxEvent.EventType,
            ["source"] = source,
            ["subject"] = $"sites/{outboxEvent.SiteId}",
            ["time"] = outboxEvent.CreatedAt.ToString("O"),
            ["datacontenttype"] = "application/json",
            ["data"] = JsonSerializer.SerializeToNode(envelope),
        };
    }
}
