using Microsoft.Extensions.Logging;
using OrderFlow.Messaging.Contracts.Correlation;
using OrderFlow.Orders.Application.Interfaces;

namespace OrderFlow.Orders.Infrastructure.Messaging;

public class LoggingEventPublisher : IEventPublisher
{
    private readonly ILogger<LoggingEventPublisher> _logger;
    private readonly ICorrelationContextAccessor? _correlationContextAccessor;

    public LoggingEventPublisher(
        ILogger<LoggingEventPublisher> logger,
        ICorrelationContextAccessor? correlationContextAccessor = null)
    {
        _logger = logger;
        _correlationContextAccessor = correlationContextAccessor;
    }

    public Task PublishAsync<T>(
        T message,
        string routingKey,
        CancellationToken cancellationToken = default) where T : class
    {
        var correlationId = _correlationContextAccessor?.CorrelationId ?? "N/A";
        _logger.LogInformation("Integration event published [RoutingKey: {RoutingKey}, CorrelationId: {CorrelationId}]: {@Message}", routingKey, correlationId, message);
        return Task.CompletedTask;
    }
}
