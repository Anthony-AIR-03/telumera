namespace Telumera.Services.SiteRegistry.Api;

/// <summary>
/// The three modules named explicitly in the M00.4 "module enablement settings" backlog task —
/// M01/M02/M03, the next modules on the roadmap. Not every module in the full CLAUDE.md roadmap
/// (M04–M09): none of those are close to being built, and this list only grows when a module
/// actually needs a toggle, same as EventTypes.cs's "don't add a constant nothing emits yet" rule.
/// </summary>
public enum Module
{
    Analytics,
    Performance,
    Errors,
}
