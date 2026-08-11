namespace OrderFlow.Orders.Application.Interfaces;

public interface IEventPublisher
{
    Task PublishAsync<T>(
        T message,
        string routingKey,
        CancellationToken cancellationToken = default) where T : class;
}
