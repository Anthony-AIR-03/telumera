using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

using Telumera.EventContracts;
using Telumera.Idempotency;

namespace Telumera.Services.Analytics.Api;

/// <summary>Consumer-side shape of analytics.page-view.received.v1 / analytics.custom-event.received.v1's payload — deliberately duplicated from event-collector's own internal record (decoupled by design, same as any other event consumer).</summary>
internal sealed record AnalyticsEventPayload(
    string Name, string SessionId, string? VisitorId, string Url, string? Environment,
    Dictionary<string, JsonElement>? Properties, string ClientTimestamp, DateTimeOffset ReceivedAt,
    string TruncatedIp, string UserAgent);

/// <summary>Payload published in analytics.processed.v1 — per-event granularity (see README for why the metric-window/anomaly rollup split is deferred).</summary>
internal sealed record AnalyticsProcessedPayload(
    string EventId, string EventName, bool IsBot, DateTimeOffset ProcessedAt);

/// <summary>
/// Normalizes, enriches, dedupes, persists, and re-publishes one accepted event — see
/// services/analytics/README.md for the full pipeline rationale (ordering of the idempotency guard
/// relative to the ClickHouse write in particular).
/// </summary>
internal sealed class EventProcessor(
    AnalyticsDbContext db,
    ClickHouseWriter clickHouse,
    IGeoLookup geoLookup,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<EventProcessor> logger)
{
    private const string PublishTopic = "analytics-events";
    private const string PublishSource = "telumera.analytics";
    private const string PublishHttpClientName = "Telumera.Analytics.Publisher";

    /// <returns>true if this call should be acked (processed now, or already processed before).</returns>
    public async Task<bool> ProcessAsync(string cloudEventId, string eventType, EventEnvelope<AnalyticsEventPayload> envelope, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(cloudEventId, out var eventId))
        {
            logger.LogWarning("Ignoring event with a non-GUID CloudEvent id {EventId}.", cloudEventId);
            return true; // not retryable — malformed id, acking avoids an infinite redelivery loop
        }

        // Committed BEFORE the ClickHouse write, not after — the two stores can't share a transaction.
        // A crash between the two either loses this event (marker committed, ClickHouse write never
        // happens) or double-counts it (ClickHouse written, marker never commits); marker-first accepts
        // the former, matching this subtask's literal goal of preventing retry duplicates from
        // affecting metrics — the risk being guarded against is overcounting, not the rarer crash-window
        // undercounting. Same accepted-trade-off framing as ADR 0004 already uses for the Collector's
        // own publish path.
        if (!await db.TryBeginProcessingEventAsync(eventId, eventType, cancellationToken))
        {
            return true; // already processed — ack without redoing the write
        }
        await db.SaveChangesAsync(cancellationToken);

        var data = envelope.Data;
        var (path, queryString) = UrlNormalizer.Normalize(data.Url);
        var utm = GetObjectProperty(data.Properties, "utm");
        var categories = UserAgentClassifier.Classify(data.UserAgent);
        var isBot = BotDetector.IsBot(data.UserAgent);
        var processedAt = DateTimeOffset.UtcNow;

        var row = new AnalyticsEventRow(
            EventId: cloudEventId,
            SiteId: envelope.SiteId,
            WorkspaceId: envelope.TenantId,
            SessionId: data.SessionId,
            VisitorId: data.VisitorId,
            EventName: data.Name,
            Url: data.Url,
            Path: path,
            QueryString: queryString,
            Title: CampaignNormalizer.NormalizeField(GetStringProperty(data.Properties, "title")),
            Referrer: CampaignNormalizer.NormalizeField(GetStringProperty(data.Properties, "referrer")),
            Channel: CampaignNormalizer.NormalizeChannel(GetStringProperty(data.Properties, "channel")),
            UtmSource: CampaignNormalizer.NormalizeField(GetStringProperty(utm, "utm_source")),
            UtmMedium: CampaignNormalizer.NormalizeField(GetStringProperty(utm, "utm_medium")),
            UtmCampaign: CampaignNormalizer.NormalizeField(GetStringProperty(utm, "utm_campaign")),
            UtmTerm: CampaignNormalizer.NormalizeField(GetStringProperty(utm, "utm_term")),
            UtmContent: CampaignNormalizer.NormalizeField(GetStringProperty(utm, "utm_content")),
            DeviceCategory: categories.Device,
            BrowserCategory: categories.Browser,
            OsCategory: categories.Os,
            Country: geoLookup.CountryFor(data.TruncatedIp),
            IsBot: isBot ? 1 : 0,
            Environment: data.Environment,
            PropertiesJson: data.Properties is null ? "{}" : JsonSerializer.Serialize(data.Properties),
            ClientTimestamp: FormatTimestamp(ParseClientTimestamp(data.ClientTimestamp, data.ReceivedAt)),
            ReceivedAt: FormatTimestamp(data.ReceivedAt),
            ProcessedAt: FormatTimestamp(processedAt));

        await clickHouse.InsertEventAsync(row, cancellationToken);

        await PublishProcessedEventAsync(envelope, data, cloudEventId, isBot, processedAt, cancellationToken);

        return true;
    }

    private async Task PublishProcessedEventAsync(
        EventEnvelope<AnalyticsEventPayload> sourceEnvelope, AnalyticsEventPayload data, string sourceEventId,
        bool isBot, DateTimeOffset processedAt, CancellationToken cancellationToken)
    {
        var daprHttpPort = configuration["DAPR_HTTP_PORT"] ?? "3500";
        var publishUrl = $"http://localhost:{daprHttpPort}/v1.0/publish/pubsub/{PublishTopic}";

        var envelope = new EventEnvelope<AnalyticsProcessedPayload>(
            TenantId: sourceEnvelope.TenantId,
            SiteId: sourceEnvelope.SiteId,
            CorrelationId: sourceEnvelope.CorrelationId,
            DataVersion: 1,
            Data: new AnalyticsProcessedPayload(sourceEventId, data.Name, isBot, processedAt));

        var cloudEvent = new JsonObject
        {
            ["specversion"] = "1.0",
            ["id"] = Guid.NewGuid().ToString(),
            ["type"] = EventTypes.AnalyticsProcessedV1,
            ["source"] = PublishSource,
            ["subject"] = $"sites/{sourceEnvelope.SiteId}",
            ["time"] = processedAt.ToString("O"),
            ["datacontenttype"] = "application/json",
            ["data"] = JsonSerializer.SerializeToNode(envelope),
        };

        var httpClient = httpClientFactory.CreateClient(PublishHttpClientName);
        using var content = JsonContent.Create(cloudEvent);
        content.Headers.ContentType = new("application/cloudevents+json");

        using var response = await httpClient.PostAsync(publishUrl, content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Publishing analytics.processed.v1 failed for source event {EventId}: {StatusCode}. Not retried.",
                sourceEventId, response.StatusCode);
        }
    }

    private static string? GetStringProperty(Dictionary<string, JsonElement>? properties, string key)
    {
        if (properties is null || !properties.TryGetValue(key, out var element))
        {
            return null;
        }
        return element.ValueKind == JsonValueKind.String ? element.GetString() : null;
    }

    private static Dictionary<string, JsonElement>? GetObjectProperty(Dictionary<string, JsonElement>? properties, string key)
    {
        if (properties is null || !properties.TryGetValue(key, out var element) || element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        return element.Deserialize<Dictionary<string, JsonElement>>();
    }

    private static DateTimeOffset ParseClientTimestamp(string clientTimestamp, DateTimeOffset fallback) =>
        DateTimeOffset.TryParse(clientTimestamp, out var parsed) ? parsed : fallback;

    private static string FormatTimestamp(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.fff");
}
