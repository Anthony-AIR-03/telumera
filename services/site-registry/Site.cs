namespace Telumera.Services.SiteRegistry.Api;

/// <summary>
/// <see cref="WorkspaceId"/> is a plain value, not a database foreign key — Site Registry owns its
/// own PostgreSQL database exclusively (docs/architecture/bounded-contexts-and-data-ownership.md) and
/// never reads Identity &amp; Workspace's tables directly. Checked against the caller's actual
/// membership role via <see cref="MembershipClient"/> rather than a database join. A site's browser
/// ingestion tokens live separately in <see cref="SiteToken"/>, not on this entity.
/// </summary>
public sealed class Site
{
    public Guid Id { get; init; }

    public required Guid WorkspaceId { get; init; }

    public required string Name { get; set; }

    public required string CanonicalDomain { get; set; }

    public required string[] AllowedOrigins { get; set; }

    public required string Environment { get; set; }

    public DateTimeOffset CreatedAt { get; init; }
}
