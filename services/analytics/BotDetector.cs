namespace Telumera.Services.Analytics.Api;

/// <summary>
/// Marks suspicious traffic rather than dropping it — privacy threat model: "Bot/duplicate events are
/// marked, not silently deleted, so data quality is inspectable."
/// </summary>
public static class BotDetector
{
    private static readonly string[] KnownBotTokens =
    [
        "bot", "spider", "crawl", "slurp", "headless", "phantomjs", "curl/", "wget/",
        "python-requests", "go-http-client", "okhttp", "postmanruntime", "axios/",
    ];

    public static bool IsBot(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return true; // no UA at all is itself a strong bot/script signal
        }

        var ua = userAgent.ToLowerInvariant();
        return KnownBotTokens.Any(ua.Contains);
    }
}
