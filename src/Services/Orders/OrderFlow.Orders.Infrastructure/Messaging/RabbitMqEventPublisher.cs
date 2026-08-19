using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderFlow.Orders.Application.Interfaces;
using RabbitMQ.Client;

namespace OrderFlow.Orders.Infrastructure.Messaging;

public class RabbitMqEventPublisher : IEventPublisher
{
    private readonly IRabbitMqConnection _connection;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqEventPublisher> _logger;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public RabbitMqEventPublisher(
        IRabbitMqConnection connection,
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqEventPublisher> logger)
    {
        _connection = connection;
        _options = options.Value;
        _logger = logger;
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

            var body = JsonSerializer.SerializeToUtf8Bytes(message, SerializerOptions);

            var properties = new BasicProperties
            {
                ContentType = "application/json",
                ContentEncoding = "utf-8",
                DeliveryMode = DeliveryModes.Persistent,
                MessageId = eventId?.ToString() ?? Guid.NewGuid().ToString(),
                CorrelationId = correlationId,
                Type = eventType ?? typeof(T).Name,
                Timestamp = new AmqpTimestamp(occurredAt?.ToUnixTimeSeconds() ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
                Headers = new Dictionary<string, object?>
                {
                    ["eventType"] = eventType ?? typeof(T).Name,
                    ["publishedAt"] = DateTimeOffset.UtcNow.ToString("o")
                }
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
            _logger.LogError(
                ex,
                "Failed to publish event of type '{MessageType}' to exchange '{Exchange}' with routing key '{RoutingKey}'.",
                typeof(T).Name,
                _options.ExchangeName,
                routingKey);

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
