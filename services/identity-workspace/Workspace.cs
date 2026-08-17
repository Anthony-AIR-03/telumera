namespace Telumera.Services.IdentityWorkspace.Api;

/// <summary>
/// Minimal tenant boundary — full membership/role/Entra ID binding is deferred to a later
/// M00.4 task (see docs/architecture/bounded-contexts-and-data-ownership.md's Identity &amp;
/// Workspace row). Only enough is modeled here to give Site Registry a WorkspaceId to own a
/// site under.
/// </summary>
public sealed class Workspace
{
    public Guid Id { get; init; }

    public required string Name { get; set; }

    public DateTimeOffset CreatedAt { get; init; }
}
