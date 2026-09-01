using System.Text.Json;

namespace Telumera.Services.EventCollector.Api;

/// <summary>
/// Bounds every event's name/property-name/property-count/property-size the same way regardless of
/// event kind — packages/browser-sdk/src/events.ts's scalar-only rule only applies to its public
/// track() API, not the SDK's own built-in page_view/engagement events (which legitimately send a
/// nested `query` object), so this checks serialized size rather than restricting value kinds.
/// </summary>
internal static class EventValidation
{
    public const int MaxEventNameLength = 100;
    public const int MaxPropertyCount = 25;
    public const int MaxPropertyKeyLength = 100;
    public const int MaxPropertyStringLength = 500;

    private static readonly string[] DenylistSubstrings = ["token", "password", "secret", "key", "auth", "session", "sid"];

    public static bool IsDenylistedPropertyName(string name)
    {
        var lower = name.ToLowerInvariant();
        return DenylistSubstrings.Any(lower.Contains);
    }

    public static IReadOnlyList<string> Validate(IncomingEvent evt)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(evt.Id)) errors.Add("id is required.");
        if (string.IsNullOrWhiteSpace(evt.SessionId)) errors.Add("sessionId is required.");
        if (string.IsNullOrWhiteSpace(evt.Url)) errors.Add("url is required.");

        if (string.IsNullOrWhiteSpace(evt.Name))
        {
            errors.Add("name must be a non-empty string.");
        }
        else if (evt.Name.Length > MaxEventNameLength)
        {
            errors.Add($"name exceeds {MaxEventNameLength} characters.");
        }

        var properties = evt.Properties ?? [];
        if (properties.Count > MaxPropertyCount)
        {
            errors.Add($"Event has more than {MaxPropertyCount} properties.");
        }

        foreach (var (key, value) in properties.Take(MaxPropertyCount))
        {
            if (key.Length > MaxPropertyKeyLength)
            {
                errors.Add($"Property name \"{key}\" exceeds {MaxPropertyKeyLength} characters.");
                continue;
            }
            if (IsDenylistedPropertyName(key))
            {
                errors.Add($"Property name \"{key}\" is blocked (matches a denylisted term).");
                continue;
            }

            // Bounded by raw serialized size rather than restricted to scalar value kinds: the SDK's
            // own built-in page_view/engagement events (unlike its public track() API, which does
            // apply the scalar-only rule client-side — see packages/browser-sdk/src/events.ts) send a
            // nested `query` object, so a scalar-only check here would reject the SDK's own traffic.
            if (value.GetRawText().Length > MaxPropertyStringLength)
            {
                errors.Add($"Property \"{key}\" exceeds {MaxPropertyStringLength} characters.");
            }
        }

        return errors;
    }
}
