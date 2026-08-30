using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Telumera.Outbox;

public static class OutboxServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="OutboxPublisher{TDbContext}"/> as a hosted service that drains
    /// <typeparamref name="TDbContext"/>'s <see cref="OutboxEvent"/> rows onto <paramref name="topic"/>.
    /// <typeparamref name="TDbContext"/> must already map <see cref="OutboxEvent"/> — call
    /// <see cref="OutboxModelBuilderExtensions.ConfigureOutboxEvent"/> from its <c>OnModelCreating</c>.
    /// </summary>
    /// <param name="topic">Dapr pub/sub topic to publish onto (one per context, not per event type).</param>
    /// <param name="source">CloudEvent <c>source</c> — e.g. <c>"telumera.site-registry"</c>.</param>
    public static IServiceCollection AddOutboxPublisher<TDbContext>(
        this IServiceCollection services, string topic, string source)
        where TDbContext : DbContext
    {
        services.Configure<OutboxPublisherOptions>(options =>
        {
            options.Topic = topic;
            options.Source = source;
        });

        services.AddHttpClient();
        services.AddHostedService<OutboxPublisher<TDbContext>>();

        return services;
    }
}
