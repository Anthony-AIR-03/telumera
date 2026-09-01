using System.Security.Cryptography;
using System.Text;

namespace Telumera.Services.EventCollector.Api;

/// <summary>
/// Definitions doc §3 — the default-mode visitor identifier: <c>hash(siteId + dailySalt + truncatedIp +
/// userAgent)</c>, computed server-side, per event, never generated or stored client-side. Never
/// persisted (this service owns "none persistent" data) — <paramref name="secret"/> plus the current
/// UTC date deterministically reproduces the same salt for every event received on the same day
/// without needing to store the salt anywhere, and produces an uncorrelatable value the next day.
/// </summary>
public static class VisitorHash
{
    public static string Compute(string secret, Guid siteId, string truncatedIp, string userAgent, DateTimeOffset now)
    {
        var dailySalt = ComputeDailySalt(secret, now);
        var input = $"{siteId}:{dailySalt}:{truncatedIp}:{userAgent}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash);
    }

    private static string ComputeDailySalt(string secret, DateTimeOffset now)
    {
        var dateString = now.UtcDateTime.ToString("yyyy-MM-dd");
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(dateString));
        return Convert.ToHexString(hash);
    }
}
