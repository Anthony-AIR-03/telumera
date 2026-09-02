using Microsoft.Identity.Web;

namespace Telumera.Services.Analytics.Api;

/// <summary>One shared 404-then-403 check reused by every query endpoint in AnalyticsQueryEndpoints.cs, instead of repeating the SiteLookupClient + MembershipClient sequence seven times.</summary>
public sealed record SiteAuthorizationResult(IResult? Error, Guid WorkspaceId, Role Role);

public static class QueryAuthorization
{
    /// <summary>
    /// Same ordering every existing endpoint in this repo already uses (site-registry, identity-workspace):
    /// resource-exists check first (404 if not), membership check second (403 if not at least Viewer) —
    /// replicated as-is, not changed. See services/site-registry/Program.cs's GetSite endpoint for the
    /// precedent this mirrors.
    /// </summary>
    public static async Task<SiteAuthorizationResult> AuthorizeSiteReadAsync(
        HttpContext httpContext, Guid siteId, SiteLookupClient siteLookupClient, MembershipClient membershipClient,
        CancellationToken cancellationToken)
    {
        var workspaceId = await siteLookupClient.GetWorkspaceIdAsync(siteId, cancellationToken);
        if (workspaceId is null)
        {
            return new SiteAuthorizationResult(Results.NotFound(), Guid.Empty, default);
        }

        var callerObjectId = httpContext.User.GetObjectId()
            ?? throw new InvalidOperationException("Token has no oid claim.");
        var role = await membershipClient.GetRoleAsync(workspaceId.Value, callerObjectId, cancellationToken);
        if (role is null)
        {
            return new SiteAuthorizationResult(Results.StatusCode(StatusCodes.Status403Forbidden), Guid.Empty, default);
        }

        return new SiteAuthorizationResult(null, workspaceId.Value, role.Value);
    }
}
