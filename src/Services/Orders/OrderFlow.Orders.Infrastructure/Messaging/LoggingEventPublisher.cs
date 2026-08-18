using Microsoft.Extensions.Logging;
using OrderFlow.Orders.Application.Interfaces;

namespace OrderFlow.Orders.Infrastructure.Messaging;

public class LoggingEventPublisher : IEventPublisher
{
    private readonly ILogger<LoggingEventPublisher> _logger;

    public LoggingEventPublisher(ILogger<LoggingEventPublisher> logger)
    {
        _logger = logger;
    }

    public Task PublishAsync<T>(
        T message,
        string routingKey,
        CancellationToken cancellationToken = default) where T : class
    {
        _logger.LogInformation("Integration event published [RoutingKey: {RoutingKey}]: {@Message}", routingKey, message);
        return Task.CompletedTask;
    }
}
