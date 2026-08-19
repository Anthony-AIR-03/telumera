namespace Telumera.Services.SiteRegistry.Api;

/// <summary>
/// Mirrors identity-workspace's Role enum (services/identity-workspace/Role.cs) — each service owns
/// its own model (no shared business database), so this is duplicated rather than imported. Ordered
/// so <c>&gt;=</c> comparisons express "at least this role."
/// </summary>
public enum Role
{
    Viewer = 0,
    Developer = 1,
    Admin = 2,
    Owner = 3,
}
