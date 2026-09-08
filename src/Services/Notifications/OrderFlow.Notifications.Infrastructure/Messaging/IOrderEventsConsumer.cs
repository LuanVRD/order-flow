using RabbitMQ.Client;

namespace OrderFlow.Notifications.Infrastructure.Messaging;

public interface IOrderEventsConsumer
{
    Task StartConsumingAsync(CancellationToken cancellationToken = default);
    Task ProcessMessageAsync(
        ReadOnlyMemory<byte> body,
        IReadOnlyBasicProperties properties,
        ulong deliveryTag,
        IChannel channel,
        CancellationToken cancellationToken = default);
}
