namespace Telumera.Services.SiteRegistry.Api;

/// <summary>
/// Per-site enabled/disabled state for one module (docs/architecture/bounded-contexts-and-data-ownership.md
/// — Site Registry owns "settings, enabled-module flags"). One row per (SiteId, Module) pair, created
/// for every <see cref="Module"/> value when a site is registered — see the POST /sites handler.
/// </summary>
public sealed class SiteModuleSetting
{
    public Guid Id { get; init; }

    public required Guid SiteId { get; init; }

    public required Module Module { get; init; }

    public required bool Enabled { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
