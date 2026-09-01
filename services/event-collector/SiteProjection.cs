using System.Collections.Concurrent;

namespace Telumera.Services.EventCollector.Api;

/// <summary>
/// The Collector's in-memory read-model projection of Site Registry data (token, allowed origins,
/// enabled modules) — docs/architecture/bounded-contexts-and-data-ownership.md's Event Collector row
/// says this service owns "none persistent", so this is deliberately never written to disk: it's
/// rebuilt from scratch on every restart via <see cref="WarmUpAsync"/> and kept current afterwards by
/// the site.created.v1/site.settings.changed.v1/site.key.rotated.v1 subscription handlers wired in
/// Program.cs. A token this projection doesn't know about falls back to a live lookup
/// (<see cref="SiteRegistryClient.LookupTokenAsync"/>) — the only case c4-container.md calls out for a
/// direct call instead of the projection.
///
/// Known gap, not fixed here (out of scope for this epic — would mean changing an already-shipped
/// M00.4 endpoint's event-publishing behavior): site-registry's POST /sites doesn't publish a
/// site.key.rotated.v1 for a brand-new site's *initial* token, only for later rotate/revoke calls — so
/// a site's first token is only ever discovered via <see cref="WarmUpAsync"/> or the cache-miss
/// fallback, never via a subscription event. Functionally harmless (every request still resolves
/// correctly, just via the slower live-lookup path until the next warm-up), but worth knowing before
/// assuming the projection is ever fully event-driven.
/// </summary>
public sealed class SiteProjection(SiteRegistryClient client, ILogger<SiteProjection> logger)
{
    private sealed class SiteRecord
    {
        public required Guid WorkspaceId;
        public required string[] AllowedOrigins;
        public required string[] EnabledModules;
        public readonly HashSet<string> Tokens = [];
    }

    private readonly ConcurrentDictionary<Guid, SiteRecord> _sitesById = new();
    private readonly ConcurrentDictionary<string, Guid> _siteIdByToken = new();

    /// <summary>Returns whether the warm-up succeeded — <see cref="SiteProjectionSyncService"/> retries on failure.</summary>
    public async Task<bool> WarmUpAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var tokens = await client.ListActiveTokensAsync(cancellationToken);
            foreach (var entry in tokens)
            {
                UpsertToken(entry);
            }
            logger.LogInformation(
                "Site projection synced: {SiteCount} sites, {TokenCount} active tokens.",
                _sitesById.Count, tokens.Count);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Site projection sync failed — the cache-miss fallback covers individual requests " +
                "in the meantime; will retry.");
            return false;
        }
    }

    public async Task<SiteTokenLookup?> ResolveAsync(string token, CancellationToken cancellationToken = default)
    {
        if (_siteIdByToken.TryGetValue(token, out var siteId) && _sitesById.TryGetValue(siteId, out var record))
        {
            return new SiteTokenLookup(token, siteId, record.WorkspaceId, record.AllowedOrigins, record.EnabledModules);
        }

        var result = await client.LookupTokenAsync(token, cancellationToken);
        if (result is null)
        {
            return null;
        }

        var resolved = result with { Token = token };
        UpsertToken(resolved);
        return resolved;
    }

    private void UpsertToken(SiteTokenLookup entry)
    {
        var record = _sitesById.AddOrUpdate(entry.SiteId,
            _ => new SiteRecord { WorkspaceId = entry.WorkspaceId, AllowedOrigins = entry.AllowedOrigins, EnabledModules = entry.EnabledModules },
            (_, existing) =>
            {
                existing.AllowedOrigins = entry.AllowedOrigins;
                existing.EnabledModules = entry.EnabledModules;
                return existing;
            });
        lock (record.Tokens)
        {
            record.Tokens.Add(entry.Token);
        }
        _siteIdByToken[entry.Token] = entry.SiteId;
    }

    /// <summary>Applies a site.created.v1 event — see the class doc comment for why this alone doesn't add a resolvable token.</summary>
    public void ApplySiteCreated(Guid siteId, Guid workspaceId, string[] allowedOrigins)
    {
        _sitesById.AddOrUpdate(siteId,
            _ => new SiteRecord { WorkspaceId = workspaceId, AllowedOrigins = allowedOrigins, EnabledModules = [] },
            (_, existing) =>
            {
                existing.AllowedOrigins = allowedOrigins;
                return existing;
            });
    }

    /// <summary>Applies a site.settings.changed.v1 event. A no-op if the site isn't in the projection yet — the next resync/cache-miss will pick it up.</summary>
    public void ApplySettingsChanged(Guid siteId, string module, bool enabled)
    {
        if (!_sitesById.TryGetValue(siteId, out var record))
        {
            return;
        }

        lock (record)
        {
            var modules = record.EnabledModules.ToHashSet();
            if (enabled) modules.Add(module); else modules.Remove(module);
            record.EnabledModules = [.. modules];
        }
    }

    /// <summary>Applies a site.key.rotated.v1 event (Action is "issued" or "revoked", see site-registry's README).</summary>
    public void ApplyKeyRotated(Guid siteId, string token, string action)
    {
        if (action == "revoked")
        {
            _siteIdByToken.TryRemove(token, out _);
            if (_sitesById.TryGetValue(siteId, out var revokedRecord))
            {
                lock (revokedRecord.Tokens)
                {
                    revokedRecord.Tokens.Remove(token);
                }
            }
            return;
        }

        if (!_sitesById.TryGetValue(siteId, out var record))
        {
            return; // site.created.v1 not seen/synced yet — next resync/cache-miss will pick it up
        }

        lock (record.Tokens)
        {
            record.Tokens.Add(token);
        }
        _siteIdByToken[token] = siteId;
    }
}
