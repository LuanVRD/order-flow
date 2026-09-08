using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderFlow.Messaging.Contracts.Correlation;
using OrderFlow.Orders.Application.Interfaces;
using RabbitMQ.Client;

namespace OrderFlow.Orders.Infrastructure.Messaging;

public class RabbitMqEventPublisher : IEventPublisher
{
    private readonly IRabbitMqConnection _connection;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqEventPublisher> _logger;
    private readonly ICorrelationContextAccessor? _correlationContextAccessor;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public RabbitMqEventPublisher(
        IRabbitMqConnection connection,
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqEventPublisher> logger,
        ICorrelationContextAccessor? correlationContextAccessor = null)
    {
        _connection = connection;
        _options = options.Value;
        _logger = logger;
        _correlationContextAccessor = correlationContextAccessor;
    }

    public async Task PublishAsync<T>(
        T message,
        string routingKey,
        CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(routingKey);

        try
        {
            await _connection.InitializeTopologyAsync(cancellationToken);

            var (eventId, eventType, correlationId, occurredAt) = ExtractMetadata(message);
            var effectiveCorrelationId = !string.IsNullOrWhiteSpace(correlationId)
                ? correlationId
                : _correlationContextAccessor?.CorrelationId;

            var body = JsonSerializer.SerializeToUtf8Bytes(message, SerializerOptions);

            var headers = new Dictionary<string, object?>
            {
                ["eventType"] = eventType ?? typeof(T).Name,
                ["publishedAt"] = DateTimeOffset.UtcNow.ToString("o")
            };

            if (!string.IsNullOrWhiteSpace(effectiveCorrelationId))
            {
                headers[CorrelationConstants.HeaderName] = effectiveCorrelationId;
                headers[CorrelationConstants.PropertyName] = effectiveCorrelationId;
            }

            var properties = new BasicProperties
            {
                ContentType = "application/json",
                ContentEncoding = "utf-8",
                DeliveryMode = DeliveryModes.Persistent,
                MessageId = eventId?.ToString() ?? Guid.NewGuid().ToString(),
                CorrelationId = effectiveCorrelationId,
                Type = eventType ?? typeof(T).Name,
                Timestamp = new AmqpTimestamp(occurredAt?.ToUnixTimeSeconds() ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
                Headers = headers
            };

            using var channel = await _connection.CreateChannelAsync(cancellationToken);

            await channel.BasicPublishAsync(
                exchange: _options.ExchangeName,
                routingKey: routingKey,
                mandatory: false,
                basicProperties: properties,
                body: body,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Integration event '{EventType}' successfully published to exchange '{Exchange}' with routing key '{RoutingKey}' [EventId: {EventId}, CorrelationId: {CorrelationId}].",
                properties.Type,
                _options.ExchangeName,
                routingKey,
                properties.MessageId,
                properties.CorrelationId ?? "N/A");
        }
        catch (Exception ex)
        {
            var (_, eventType, correlationId, _) = ExtractMetadata(message);
            var effectiveCorrId = !string.IsNullOrWhiteSpace(correlationId)
                ? correlationId
                : _correlationContextAccessor?.CorrelationId;

            _logger.LogError(
                ex,
                "Failed to publish integration event '{EventType}' to exchange '{Exchange}' with routing key '{RoutingKey}' [CorrelationId: {CorrelationId}].",
                eventType ?? typeof(T).Name,
                _options.ExchangeName,
                routingKey,
                effectiveCorrId ?? "N/A");

            throw;
        }
    }

    private static (Guid? eventId, string? eventType, string? correlationId, DateTimeOffset? occurredAt) ExtractMetadata<T>(T message)
    {
        if (message == null)
        {
            return (null, null, null, null);
        }

        var messageType = message.GetType();
        
        Guid? eventId = null;
        string? eventType = null;
        string? correlationId = null;
        DateTimeOffset? occurredAt = null;

        var eventIdProp = messageType.GetProperty("EventId");
        if (eventIdProp?.GetValue(message) is Guid id)
        {
            eventId = id;
        }

        var eventTypeProp = messageType.GetProperty("EventType");
        if (eventTypeProp?.GetValue(message) is string type)
        {
            eventType = type;
        }

        var correlationIdProp = messageType.GetProperty("CorrelationId");
        if (correlationIdProp?.GetValue(message) is string corrId)
        {
            correlationId = corrId;
        }

        var occurredAtProp = messageType.GetProperty("OccurredAt");
        if (occurredAtProp?.GetValue(message) is DateTimeOffset time)
        {
            occurredAt = time;
        }

        return (eventId, eventType, correlationId, occurredAt);
    }
}
