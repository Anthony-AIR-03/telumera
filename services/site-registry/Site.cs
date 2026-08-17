namespace Telumera.Services.SiteRegistry.Api;

/// <summary>
/// <see cref="WorkspaceId"/> is a plain value, not a database foreign key — Site Registry owns its
/// own PostgreSQL database exclusively (docs/architecture/bounded-contexts-and-data-ownership.md) and
/// never reads Identity &amp; Workspace's tables directly. It is not yet validated against that
/// service's API either: real validation arrives once a workspace-authenticated caller (Entra ID,
/// deferred) puts a trusted workspace context on the request.
/// </summary>
public sealed class Site
{
    public Guid Id { get; init; }

    public required Guid WorkspaceId { get; init; }

    public required string Name { get; set; }

    public required string CanonicalDomain { get; set; }

    public required string[] AllowedOrigins { get; set; }

    public required string Environment { get; set; }

    /// <summary>
    /// Public browser ingestion token (docs/adr/0006-public-browser-ingestion-tokens.md) — public by
    /// design, not a secret, so it is stored in plaintext rather than hashed.
    /// </summary>
    public required string BrowserToken { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}
