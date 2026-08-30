namespace Telumera.Outbox;

public sealed class OutboxPublisherOptions
{
    /// <summary>
    /// Dapr pub/sub topic this service's events publish under. One topic per *context*, not per event
    /// type — see the owning service's README for why (e.g. services/site-registry/README.md).
    /// </summary>
    public string Topic { get; set; } = string.Empty;

    /// <summary>CloudEvent <c>source</c> — e.g. <c>"telumera.site-registry"</c>.</summary>
    public string Source { get; set; } = string.Empty;
}
