namespace Telumera.Services.SiteRegistry.Api;

/// <summary>
/// Public browser ingestion token (docs/adr/0006-public-browser-ingestion-tokens.md) — public by
/// design, not a secret, so it is stored in plaintext rather than hashed. A site can have several of
/// these over its lifetime: issuing a new one (rotation) never revokes existing ones automatically,
/// so an old and new token can be valid simultaneously during a safe migration — revocation is a
/// separate, explicit action.
/// </summary>
public sealed class SiteToken
{
    public Guid Id { get; init; }

    public required Guid SiteId { get; init; }

    public required string Token { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? RevokedAt { get; set; }
}
