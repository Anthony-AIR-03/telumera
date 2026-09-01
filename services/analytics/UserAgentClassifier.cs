namespace Telumera.Services.Analytics.Api;

public sealed record UserAgentCategories(string Device, string Browser, string Os);

/// <summary>
/// Hand-rolled substring heuristics into bounded categories — not a UA-parsing NuGet library, which
/// would produce more entropy than the privacy threat model's "bounded categories (e.g. 'mobile /
/// Chrome / Android')... rather than storing full high-entropy fingerprint strings" row wants. Coarse
/// is the correct target here, not a limitation to work around later.
/// </summary>
public static class UserAgentClassifier
{
    public static UserAgentCategories Classify(string? userAgent)
    {
        var ua = userAgent?.ToLowerInvariant() ?? string.Empty;

        var os = ua switch
        {
            _ when ua.Contains("windows") => "Windows",
            _ when ua.Contains("mac os") || ua.Contains("macintosh") => "macOS",
            _ when ua.Contains("android") => "Android",
            _ when ua.Contains("iphone") || ua.Contains("ipad") || ua.Contains("ios") => "iOS",
            _ when ua.Contains("linux") => "Linux",
            _ => "Other",
        };

        // Order matters: check tablet/iPad before the broader mobile check, since an iPad's UA can
        // also contain "Mobile" — and "Chrome" checks must precede "Safari", since Chrome's own UA
        // string always includes a trailing "Safari/537.36" for compatibility.
        var device = ua switch
        {
            _ when ua.Contains("ipad") || ua.Contains("tablet") => "Tablet",
            _ when ua.Contains("mobi") || ua.Contains("android") => "Mobile",
            _ when ua.Length == 0 => "Other",
            _ => "Desktop",
        };

        var browser = ua switch
        {
            _ when ua.Contains("edg/") => "Edge",
            _ when ua.Contains("chrome/") => "Chrome",
            _ when ua.Contains("firefox/") => "Firefox",
            _ when ua.Contains("safari/") => "Safari",
            _ => "Other",
        };

        return new UserAgentCategories(device, browser, os);
    }
}
