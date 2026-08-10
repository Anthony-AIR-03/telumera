namespace Telumera.EventContracts;

/// <summary>
/// CloudEvent <c>type</c> values for the initial event catalogue
/// (see <c>planning/Telumera_Modular_Project_Plan.md</c> §8.3, mirrored in
/// <c>docs/architecture/c4-container.md</c>). Extend this list as each module defines new events —
/// don't add a constant for an event no publisher emits yet.
/// </summary>
public static class EventTypes
{
    public const string SiteCreatedV1 = "site.created.v1";
    public const string SiteSettingsChangedV1 = "site.settings.changed.v1";
    public const string AnalyticsPageViewReceivedV1 = "analytics.page-view.received.v1";
    public const string AnalyticsCustomEventReceivedV1 = "analytics.custom-event.received.v1";
    public const string AnalyticsProcessedV1 = "analytics.processed.v1";
    public const string PerformanceSampleReceivedV1 = "performance.sample.received.v1";
    public const string ErrorOccurrenceReceivedV1 = "error.occurrence.received.v1";
    public const string ErrorIssueChangedV1 = "error.issue.changed.v1";
    public const string DeploymentCompletedV1 = "deployment.completed.v1";
    public const string SeoScanCompletedV1 = "seo.scan.completed.v1";
    public const string InsightGeneratedV1 = "insight.generated.v1";
    public const string NotificationRequestedV1 = "notification.requested.v1";
}
