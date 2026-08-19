namespace Telumera.Services.IdentityWorkspace.Api;

/// <summary>
/// Ordered so <c>&gt;=</c> comparisons express "at least this role" (e.g. checking
/// <c>membership.Role &gt;= Role.Developer</c> passes for Developer, Admin, or Owner).
/// </summary>
public enum Role
{
    Viewer = 0,
    Developer = 1,
    Admin = 2,
    Owner = 3,
}
