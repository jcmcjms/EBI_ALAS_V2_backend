using EBI.ALAS.Api.Infrastructure.Messaging.Events;
using MassTransit;

namespace EBI.ALAS.Api.Infrastructure.Messaging;

/// <summary>
/// Abstraction over MassTransit IPublishEndpoint for publishing
/// integration events. Keeps domain code decoupled from the
/// message broker implementation.
/// </summary>
public interface IEventPublisher
{
    Task PublishAsync<T>(T message, CancellationToken ct = default) where T : class;
}

/// <summary>
/// MassTransit-backed event publisher. Publishes messages to the
/// configured message broker (RabbitMQ) for async consumption.
/// </summary>
public sealed class MassTransitEventPublisher : IEventPublisher
{
    private readonly IPublishEndpoint _publishEndpoint;

    public MassTransitEventPublisher(IPublishEndpoint publishEndpoint)
    {
        _publishEndpoint = publishEndpoint;
    }

    public async Task PublishAsync<T>(T message, CancellationToken ct = default) where T : class
    {
        await _publishEndpoint.Publish(message, ct);
    }
}
