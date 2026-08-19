namespace Telumera.Services.IdentityWorkspace.Api;

/// <summary>One user's role within one workspace. Unique per (UserId, WorkspaceId) pair.</summary>
public sealed class Membership
{
    public Guid Id { get; init; }

    public required Guid UserId { get; init; }

    public required Guid WorkspaceId { get; init; }

    public required Role Role { get; set; }

    public DateTimeOffset CreatedAt { get; init; }
}
