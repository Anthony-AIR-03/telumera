namespace Telumera.Services.Analytics.Api;

/// <summary>
/// Server-side mirror of packages/browser-sdk/src/url.ts's canonicalization rules (strip fragment,
/// strip trailing slash except root), applied to the trusted `Url` field rather than trusting
/// client-echoed `properties.path` verbatim — defense in depth against a bypassed/malicious client.
/// </summary>
public static class UrlNormalizer
{
    public static (string Path, string QueryString) Normalize(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return ("/", string.Empty);
        }

        var path = uri.AbsolutePath; // Uri.AbsolutePath already excludes the fragment
        if (path.Length > 1 && path.EndsWith('/'))
        {
            path = path[..^1];
        }
        if (path.Length == 0)
        {
            path = "/";
        }

        return (path, uri.Query.TrimStart('?'));
    }
}
