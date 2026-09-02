using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Telumera.Services.Analytics.Api;

/// <summary>
/// Periodically reads the depth of the analytics dead-letter queue
/// (<c>dlq-analytics-collector-events</c> — Dapr's <c>dlq-&lt;app-id&gt;-&lt;topic&gt;</c> convention,
/// same queue <c>tools/dead-letter-recovery</c> inspects) via RabbitMQ's management HTTP API and
/// stashes it in <see cref="DeadLetterGauge"/> for the data-quality endpoint's <c>failed</c> figure.
/// A point-in-time gauge, not a per-day dimension — a message sitting in the DLQ is a "still failing"
/// signal, not a historical count, so it isn't written into <c>event_quality_daily</c>.
/// </summary>
public sealed class DeadLetterProbe(
    DeadLetterGauge gauge,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<DeadLetterProbe> logger) : BackgroundService
{
    private const string QueueName = "dlq-analytics-collector-events";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var managementUrl = (configuration["Quality:RabbitMqManagementUrl"] ?? "http://rabbitmq:15672").TrimEnd('/');
        var user = configuration["Quality:RabbitMqUser"] ?? "telumera";
        var password = configuration["Quality:RabbitMqPassword"] ?? string.Empty;
        var intervalSeconds = int.TryParse(configuration["Quality:DeadLetterProbeSeconds"], out var s) && s > 0 ? s : 30;

        var httpClient = httpClientFactory.CreateClient(nameof(DeadLetterProbe));
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}")));
        var url = $"{managementUrl}/api/queues/%2F/{QueueName}";

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(intervalSeconds));
        do
        {
            try
            {
                using var response = await httpClient.GetAsync(url, stoppingToken);
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    gauge.Set(0); // queue not created yet => nothing has ever dead-lettered
                    continue;
                }
                response.EnsureSuccessStatusCode();

                await using var stream = await response.Content.ReadAsStreamAsync(stoppingToken);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: stoppingToken);
                gauge.Set(document.RootElement.TryGetProperty("messages", out var messages) ? messages.GetInt64() : 0);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Dead-letter depth probe failed; keeping the last known value.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
