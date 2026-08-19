using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;

namespace Telumera.Services.IdentityWorkspace.Api;

/// <summary>
/// JIT-provisions the caller's <see cref="User"/> row from their validated Entra ID token — there is
/// no separate signup flow anywhere in this platform, so the first authenticated call from a given
/// Entra Object ID creates it. Also opportunistically refreshes DisplayName/Email from the token's
/// claims on every call, so profile info fills in as soon as someone actually signs in, even if a
/// stub row already exists from being added as a member before they ever authenticated.
/// </summary>
public sealed class CurrentUserAccessor(IHttpContextAccessor httpContextAccessor, IdentityWorkspaceDbContext db)
{
    public async Task<User> GetOrProvisionAsync(CancellationToken cancellationToken = default)
    {
        var principal = httpContextAccessor.HttpContext?.User
            ?? throw new InvalidOperationException("No HttpContext available.");

        var entraObjectId = principal.GetObjectId()
            ?? throw new InvalidOperationException("Token has no oid claim.");

        var user = await db.Users.FirstOrDefaultAsync(u => u.EntraObjectId == entraObjectId, cancellationToken);

        var displayName = principal.FindFirst("name")?.Value;
        var email = principal.GetLoginHint() ?? principal.FindFirst("preferred_username")?.Value;

        if (user is null)
        {
            user = new User
            {
                Id = Guid.NewGuid(),
                EntraObjectId = entraObjectId,
                DisplayName = displayName,
                Email = email,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            db.Users.Add(user);
        }
        else
        {
            user.DisplayName = displayName ?? user.DisplayName;
            user.Email = email ?? user.Email;
        }

        await db.SaveChangesAsync(cancellationToken);
        return user;
    }
}
