using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

// M01.8 "Run synthetic acceptance traffic" — drives the REAL SDK→collector→pipeline path (unlike
// seed-demo-data.sh, which writes fake bootstrap_* rows straight into the DBs). Sends a fixed set of
// scripted journeys through POST /v1/events and prints the metric deltas they should produce, so a
// run can be reconciled against the dashboard + tools/reconciliation-report.
//
//   dotnet run --project tools/synthetic-traffic -- --token <siteToken> [--collector http://localhost:5103] [--origin https://example.com]

var collector = (GetOption(args, "--collector") ?? "http://localhost:5103").TrimEnd('/');
var token = GetOption(args, "--token")
    ?? throw new InvalidOperationException("--token <siteToken> is required (rotate one in the dashboard).");
var origin = GetOption(args, "--origin");

using var http = new HttpClient { BaseAddress = new Uri(collector) };

var expected = new Dictionary<string, long>();
void Expect(string dimension, long n) => expected[dimension] = expected.GetValueOrDefault(dimension) + n;

// --- Journey 1: an engaged session (landing + 2 pages + engagement + a custom event) ------------
var s1 = Guid.NewGuid().ToString();
await SendBatch("engaged session", token, origin,
[
    PageView(s1, "/", "Home"),
    PageView(s1, "/pricing", "Pricing"),
    PageView(s1, "/signup", "Sign up"),
    Engagement(s1, 15_000),
    Custom(s1, "signup_completed", new { plan = "pro" }),
]);
Expect("accepted", 5);
Console.WriteLine("  → 1 engaged session, 3 page views on / /pricing /signup");

// --- Journey 2: a bounce (single page view) ---------------------------------------------------
var s2 = Guid.NewGuid().ToString();
await SendBatch("bounce", token, origin, [PageView(s2, "/blog/post", "A post")]);
Expect("accepted", 1);
Console.WriteLine("  → 1 non-engaged session, 1 page view on /blog/post");

// --- Journey 3: a bot (bot User-Agent header) -----------------------------------------------
var s3 = Guid.NewGuid().ToString();
await SendBatch("bot", token, origin, [PageView(s3, "/", "Home")],
    userAgent: "Googlebot/2.1 (+http://www.google.com/bot.html)");
Expect("accepted", 1);
Expect("bot", 1);
Console.WriteLine("  → 1 event stored with is_bot = 1 (kept, not dropped)");

// --- Journey 4: a duplicate (same event id twice) -----------------------------------------
var dupEvent = PageView(Guid.NewGuid().ToString(), "/", "Home");
await SendBatch("duplicate (first)", token, origin, [dupEvent]);
await SendBatch("duplicate (second)", token, origin, [dupEvent]);
Expect("accepted", 1);
Expect("duplicate", 1);
Console.WriteLine("  → second submission deduped, no extra ClickHouse row");

// --- Journey 5: a malformed event (26 properties > the 25 cap) -----------------------------
var manyProps = new Dictionary<string, object>();
for (var i = 0; i < 26; i++) manyProps[$"p{i}"] = i;
await SendBatch("malformed", token, origin,
[
    new Dictionary<string, object?>
    {
        ["id"] = Guid.NewGuid().ToString(), ["name"] = "custom", ["siteToken"] = token,
        ["sessionId"] = Guid.NewGuid().ToString(), ["url"] = "https://example.com/x",
        ["timestamp"] = DateTimeOffset.UtcNow.ToString("O"), ["properties"] = manyProps,
    },
]);
Expect("rejected_validation", 1);
Console.WriteLine("  → rejected at the collector, the batch still returns 2xx");

// --- Journey 6: an unknown token (bogus siteToken) --------------------------------------
var bogus = "telum_" + Guid.NewGuid().ToString("N");
await SendBatch("unknown token", bogus, origin, [PageView(Guid.NewGuid().ToString(), "/", "Home")],
    expectStatus: HttpStatusCode.NotFound);
Expect("rejected_unknown_token", 1);
Console.WriteLine("  → 404, counted under the platform (no site attribution possible)");

Console.WriteLine();
Console.WriteLine("Expected metric deltas for this run:");
foreach (var (dimension, n) in expected.OrderBy(kv => kv.Key))
{
    Console.WriteLine($"  {dimension,-24} {n}");
}
Console.WriteLine();
Console.WriteLine("Verify with: the site's analytics + data-quality screens, and");
Console.WriteLine("  dotnet run --project tools/reconciliation-report -- --date " + DateTime.UtcNow.ToString("yyyy-MM-dd"));

return 0;

async Task SendBatch(
    string label, string batchToken, string? originHeader, IReadOnlyList<object> events,
    string? userAgent = null, HttpStatusCode expectStatus = HttpStatusCode.Accepted)
{
    foreach (var evt in events)
    {
        if (evt is IDictionary<string, object?> map)
        {
            map["siteToken"] = batchToken;
        }
    }

    using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/events")
    {
        Content = JsonContent.Create(new { events }),
    };
    if (originHeader is not null) request.Headers.TryAddWithoutValidation("Origin", originHeader);
    if (userAgent is not null) request.Headers.TryAddWithoutValidation("User-Agent", userAgent);

    using var response = await http.SendAsync(request);
    var body = await response.Content.ReadAsStringAsync();
    var ok = response.StatusCode == expectStatus;
    Console.WriteLine($"[{(ok ? "ok" : "??")}] {label,-22} {(int)response.StatusCode} {response.StatusCode}  {body.Trim()}");
    if (!ok)
    {
        Console.Error.WriteLine($"  expected {(int)expectStatus} {expectStatus}");
    }
}

static Dictionary<string, object?> PageView(string sessionId, string path, string title) => new()
{
    ["id"] = Guid.NewGuid().ToString(),
    ["name"] = "page_view",
    ["siteToken"] = "", // filled per-batch below is unnecessary; the collector reads it from each event
    ["sessionId"] = sessionId,
    ["url"] = "https://example.com" + path,
    ["timestamp"] = DateTimeOffset.UtcNow.ToString("O"),
    ["properties"] = new Dictionary<string, object?>
    {
        ["title"] = title,
        ["channel"] = "direct",
        ["query"] = new Dictionary<string, string>(),
    },
};

static Dictionary<string, object?> Engagement(string sessionId, int activeMs) => new()
{
    ["id"] = Guid.NewGuid().ToString(),
    ["name"] = "engagement",
    ["siteToken"] = "",
    ["sessionId"] = sessionId,
    ["url"] = "https://example.com/",
    ["timestamp"] = DateTimeOffset.UtcNow.ToString("O"),
    ["properties"] = new Dictionary<string, object?> { ["activeMs"] = activeMs },
};

static Dictionary<string, object?> Custom(string sessionId, string name, object props) => new()
{
    ["id"] = Guid.NewGuid().ToString(),
    ["name"] = name,
    ["siteToken"] = "",
    ["sessionId"] = sessionId,
    ["url"] = "https://example.com/",
    ["timestamp"] = DateTimeOffset.UtcNow.ToString("O"),
    ["properties"] = props,
};

static string? GetOption(string[] args, string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}
