using System.Text;
using System.Text.Json;

using NBomber.CSharp;
using NBomber.Http.CSharp;

// M01.3's "Add collector load test" subtask — measures POST /v1/events throughput/latency against a
// realistic batch shape. Not wired into CI (needs the full local Compose stack running, same as
// tests/integration/) — run manually: `dotnet run --project tests/load/Telumera.LoadTests`.
//
// Requires a real, already-issued site token (event-collector has no way to mint one itself — that's
// site-registry's job, and it requires a real Entra sign-in). Obtain one via the normal dashboard flow
// or site-registry's POST /sites, then set TELUMERA_LOAD_TEST_SITE_TOKEN.
var siteToken = Environment.GetEnvironmentVariable("TELUMERA_LOAD_TEST_SITE_TOKEN")
    ?? throw new InvalidOperationException(
        "TELUMERA_LOAD_TEST_SITE_TOKEN is not set — register a site via site-registry first (see " +
        "tests/integration/Telumera.Tests.Integration/EventCollectorTests.cs for how) and export its token.");

var collectorUrl = Environment.GetEnvironmentVariable("TELUMERA_LOAD_TEST_COLLECTOR_URL") ?? "http://localhost:5103";
var batchSize = int.TryParse(Environment.GetEnvironmentVariable("TELUMERA_LOAD_TEST_BATCH_SIZE"), out var b) ? b : 10;
var ratePerSecond = int.TryParse(Environment.GetEnvironmentVariable("TELUMERA_LOAD_TEST_RATE"), out var r) ? r : 50;
var durationSeconds = int.TryParse(Environment.GetEnvironmentVariable("TELUMERA_LOAD_TEST_DURATION_SECONDS"), out var d) ? d : 30;

using var httpClient = new HttpClient { BaseAddress = new Uri(collectorUrl) };

var scenario = Scenario.Create("post_events_batch", async context =>
{
    var payload = BuildBatch(siteToken, batchSize);
    var body = JsonSerializer.Serialize(payload);

    var request = Http.CreateRequest("POST", "/v1/events")
        .WithHeader("Content-Type", "application/json")
        .WithBody(new StringContent(body, Encoding.UTF8, "application/json"));

    var response = await Http.Send(httpClient, request);
    return response;
})
.WithLoadSimulations(
    Simulation.RampingInject(rate: ratePerSecond, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(10)),
    Simulation.Inject(rate: ratePerSecond, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(durationSeconds))
);

NBomberRunner
    .RegisterScenarios(scenario)
    .Run();

static object BuildBatch(string siteToken, int batchSize)
{
    var now = DateTimeOffset.UtcNow;
    var sessionId = Guid.NewGuid().ToString();

    var events = Enumerable.Range(0, batchSize).Select(i => new
    {
        Id = Guid.NewGuid().ToString(),
        Name = i == 0 ? "page_view" : "engagement",
        SiteToken = siteToken,
        Environment = "load-test",
        SessionId = sessionId,
        VisitorId = (string?)null,
        Url = "https://load-test.example/",
        Timestamp = now.ToString("O"),
        Properties = i == 0
            ? new Dictionary<string, object> { ["path"] = "/", ["query"] = new Dictionary<string, object>() }
            : new Dictionary<string, object> { ["activeMs"] = 1500 },
    });

    return new { Events = events };
}
