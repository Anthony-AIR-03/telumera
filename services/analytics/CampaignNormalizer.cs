namespace Telumera.Services.Analytics.Api;

/// <summary>
/// Validates/stabilizes the channel and free-text acquisition fields the SDK already classifies
/// client-side (packages/browser-sdk/src/campaign.ts) rather than re-implementing classification —
/// this step's job is producing stable, query-ready columns, not deciding what "search" means.
/// </summary>
public static class CampaignNormalizer
{
    private static readonly HashSet<string> KnownChannels = ["direct", "search", "social", "referral"];
    private const int MaxFieldLength = 200;

    public static string NormalizeChannel(string? channel) =>
        channel is not null && KnownChannels.Contains(channel) ? channel : "direct";

    public static string? NormalizeField(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length > MaxFieldLength ? trimmed[..MaxFieldLength] : trimmed;
    }
}
