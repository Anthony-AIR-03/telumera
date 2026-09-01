using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

// M01.4's "Create dead-letter recovery command" subtask — inspects and replays messages Dapr's
// RabbitMQ pub/sub component dead-letters after exhausting resiliency retries
// (infrastructure/dapr/components/pubsub-rabbitmq.yaml's enableDeadLetter: true;
// infrastructure/dapr/components/resiliency.yaml's bounded retry/circuit-breaker policy). Dead-letter
// queue naming follows Dapr's own convention, confirmed live in M01.3's container logs:
// dlq-<app-id>-<topic> (e.g. "dlq-analytics-collector-events").
//
// Usage:
//   dotnet run --project tools/dead-letter-recovery -- list [--queue <name>]
//   dotnet run --project tools/dead-letter-recovery -- replay [--queue <name>] [--exchange <name>] [--count N]
//
// Two subcommands, no interactive per-message editing — matches "Inspect, fix and replay failed
// messages safely" at the scope this epic's other 9 subtasks are written at, not a full operator UI.

const string DefaultQueue = "dlq-analytics-collector-events";
const string DefaultExchange = "collector-events";

var managementUrl = Environment.GetEnvironmentVariable("RABBITMQ_MANAGEMENT_URL") ?? "http://localhost:15672";
var user = Environment.GetEnvironmentVariable("RABBITMQ_DEFAULT_USER")
    ?? throw new InvalidOperationException("RABBITMQ_DEFAULT_USER is not set.");
var password = Environment.GetEnvironmentVariable("RABBITMQ_DEFAULT_PASS")
    ?? throw new InvalidOperationException("RABBITMQ_DEFAULT_PASS is not set.");

using var httpClient = new HttpClient { BaseAddress = new Uri(managementUrl) };
httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
    "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}")));

var command = args.Length > 0 ? args[0] : null;
var queue = GetOption(args, "--queue") ?? DefaultQueue;

switch (command)
{
    case "list":
        {
            var messages = await FetchMessagesAsync(httpClient, queue, count: 50, requeue: true);
            Console.WriteLine($"{messages.Count} message(s) in '{queue}':");
            foreach (var message in messages)
            {
                var body = message["payload"]?.GetValue<string>() ?? string.Empty;
                var preview = body.Length > 200 ? body[..200] + "…" : body;
                Console.WriteLine($"  - {preview}");
            }
            return 0;
        }

    case "replay":
        {
            var exchange = GetOption(args, "--exchange") ?? DefaultExchange;
            var count = int.TryParse(GetOption(args, "--count"), out var n) ? n : 50;

            var messages = await FetchMessagesAsync(httpClient, queue, count, requeue: false);
            Console.WriteLine($"Replaying {messages.Count} message(s) from '{queue}' onto exchange '{exchange}'...");

            foreach (var message in messages)
            {
                var payload = message["payload"]?.GetValue<string>() ?? string.Empty;
                await PublishAsync(httpClient, exchange, payload);
            }

            Console.WriteLine($"Replayed {messages.Count} message(s).");
            return 0;
        }

    default:
        Console.Error.WriteLine("Usage: dead-letter-recovery <list|replay> [--queue <name>] [--exchange <name>] [--count N]");
        return 1;
}

static async Task<List<JsonObject>> FetchMessagesAsync(HttpClient httpClient, string queue, int count, bool requeue)
{
    var body = JsonSerializer.Serialize(new
    {
        count,
        ackmode = requeue ? "ack_requeue_true" : "ack_requeue_false",
        encoding = "auto",
    });

    using var content = new StringContent(body, Encoding.UTF8, "application/json");
    using var response = await httpClient.PostAsync($"/api/queues/%2f/{Uri.EscapeDataString(queue)}/get", content);
    response.EnsureSuccessStatusCode();

    var json = await response.Content.ReadAsStringAsync();
    var array = JsonNode.Parse(json)?.AsArray() ?? [];
    return array.OfType<JsonObject>().ToList();
}

static async Task PublishAsync(HttpClient httpClient, string exchange, string payload)
{
    var body = JsonSerializer.Serialize(new
    {
        properties = new { },
        routing_key = string.Empty, // fanout exchange (pubsub-rabbitmq.yaml) — routing key is ignored
        payload,
        payload_encoding = "string",
    });

    using var content = new StringContent(body, Encoding.UTF8, "application/json");
    using var response = await httpClient.PostAsync($"/api/exchanges/%2f/{Uri.EscapeDataString(exchange)}/publish", content);
    response.EnsureSuccessStatusCode();
}

static string? GetOption(string[] args, string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}
