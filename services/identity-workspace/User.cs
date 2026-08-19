namespace Telumera.Services.IdentityWorkspace.Api;

/// <summary>
/// Local user profile bound to the caller's external Entra ID identity
/// (docs/architecture/bounded-contexts-and-data-ownership.md's Identity &amp; Workspace row) — not
/// passwords or authentication credentials, which Entra ID owns exclusively. JIT-provisioned by
/// <see cref="CurrentUserAccessor"/> rather than requiring a separate signup step.
/// </summary>
public sealed class User
{
    public Guid Id { get; init; }

    public required string EntraObjectId { get; init; }

    public string? DisplayName { get; set; }

    public string? Email { get; set; }

    public DateTimeOffset CreatedAt { get; init; }
}
