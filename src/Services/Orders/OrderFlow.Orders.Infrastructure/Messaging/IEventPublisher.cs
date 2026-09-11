namespace OrderFlow.Orders.Infrastructure.Messaging;

public interface IEventPublisher
{
    Task PublishAsync<T>(
        T message,
        string routingKey,
        CancellationToken cancellationToken = default) where T : class;
}
