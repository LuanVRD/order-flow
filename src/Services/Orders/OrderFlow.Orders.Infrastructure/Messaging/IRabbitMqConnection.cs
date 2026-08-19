using RabbitMQ.Client;

namespace OrderFlow.Orders.Infrastructure.Messaging;

public interface IRabbitMqConnection : IAsyncDisposable
{
    Task<IChannel> CreateChannelAsync(CancellationToken cancellationToken = default);
    Task InitializeTopologyAsync(CancellationToken cancellationToken = default);
    bool IsConnected { get; }
}
