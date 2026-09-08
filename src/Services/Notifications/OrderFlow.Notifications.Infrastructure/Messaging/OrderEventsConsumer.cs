using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderFlow.Messaging.Contracts.Correlation;
using OrderFlow.Messaging.Contracts.Events;
using OrderFlow.Notifications.Application.UseCases;
using OrderFlow.Notifications.Domain.Entities;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OrderFlow.Notifications.Infrastructure.Messaging;

public class OrderEventsConsumer : IOrderEventsConsumer
{
    private readonly IRabbitMqConnection _connection;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<OrderEventsConsumer> _logger;
    private readonly ICorrelationContextAccessor? _correlationContextAccessor;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public OrderEventsConsumer(
        IRabbitMqConnection connection,
        IServiceScopeFactory serviceScopeFactory,
        IOptions<RabbitMqOptions> options,
        ILogger<OrderEventsConsumer> logger,
        ICorrelationContextAccessor? correlationContextAccessor = null)
    {
        _connection = connection;
        _serviceScopeFactory = serviceScopeFactory;
        _options = options.Value;
        _logger = logger;
        _correlationContextAccessor = correlationContextAccessor;
    }

    public async Task StartConsumingAsync(CancellationToken cancellationToken = default)
    {
        await _connection.InitializeTopologyAsync(cancellationToken);

        var channel = await _connection.CreateChannelAsync(cancellationToken);

        await channel.BasicQosAsync(
            prefetchSize: 0,
            prefetchCount: _options.PrefetchCount,
            global: false,
            cancellationToken: cancellationToken);

        var consumer = new AsyncEventingBasicConsumer(channel);

        consumer.ReceivedAsync += async (sender, ea) =>
        {
            await ProcessMessageAsync(ea.Body, ea.BasicProperties, ea.DeliveryTag, channel, cancellationToken);
        };

        _logger.LogInformation(
            "Starting consumption on queue '{QueueName}' with PrefetchCount {PrefetchCount} and DLQ '{DlQueueName}'...",
            _options.QueueName,
            _options.PrefetchCount,
            _options.DeadLetterQueueName);

        await channel.BasicConsumeAsync(
            queue: _options.QueueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: cancellationToken);

        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Consumer on queue '{QueueName}' is shutting down gracefully...", _options.QueueName);
        }
        finally
        {
            try
            {
                await channel.CloseAsync();
                await channel.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error while closing consumer channel during shutdown.");
            }
        }
    }

    public async Task ProcessMessageAsync(
        ReadOnlyMemory<byte> body,
        IReadOnlyBasicProperties properties,
        ulong deliveryTag,
        IChannel channel,
        CancellationToken cancellationToken = default)
    {
        string? eventType = properties.Type;
        string? correlationId = properties.CorrelationId;
        string? messageId = properties.MessageId;
        Guid? eventId = Guid.TryParse(messageId, out var parsedEventId) ? parsedEventId : null;
        Guid? orderId = null;

        // Check AMQP headers if correlationId was not present on properties
        if (string.IsNullOrWhiteSpace(correlationId) && properties.Headers != null)
        {
            if (properties.Headers.TryGetValue(CorrelationConstants.HeaderName, out var headerObj) ||
                properties.Headers.TryGetValue(CorrelationConstants.PropertyName, out headerObj) ||
                properties.Headers.TryGetValue("correlationId", out headerObj))
            {
                if (headerObj is byte[] headerBytes)
                {
                    correlationId = System.Text.Encoding.UTF8.GetString(headerBytes);
                }
                else if (headerObj != null)
                {
                    correlationId = headerObj.ToString();
                }
            }
        }

        // 1. Validate & Parse JSON Payload Metadata
        try
        {
            using var jsonDoc = JsonDocument.Parse(body);
            var root = jsonDoc.RootElement;

            if (string.IsNullOrWhiteSpace(eventType))
            {
                if (root.TryGetProperty("eventType", out var etProp) || root.TryGetProperty("EventType", out etProp))
                {
                    eventType = etProp.GetString();
                }
            }

            if (!eventId.HasValue)
            {
                if ((root.TryGetProperty("eventId", out var eidProp) || root.TryGetProperty("EventId", out eidProp)) &&
                    eidProp.TryGetGuid(out var parsedEid))
                {
                    eventId = parsedEid;
                }
            }

            if (string.IsNullOrWhiteSpace(correlationId))
            {
                if (root.TryGetProperty("correlationId", out var corrProp) || root.TryGetProperty("CorrelationId", out corrProp))
                {
                    correlationId = corrProp.GetString();
                }
            }

            if (root.TryGetProperty("data", out var dataProp) || root.TryGetProperty("Data", out dataProp))
            {
                if (dataProp.ValueKind == JsonValueKind.Object &&
                    (dataProp.TryGetProperty("orderId", out var oidProp) || dataProp.TryGetProperty("OrderId", out oidProp)) &&
                    oidProp.TryGetGuid(out var parsedOid))
                {
                    orderId = parsedOid;
                }
            }
        }
        catch (JsonException ex)
        {
            var poisonCorrelationId = !string.IsNullOrWhiteSpace(correlationId) ? correlationId : "N/A";
            using var poisonScope = _logger.BeginScope(new Dictionary<string, object>
            {
                [CorrelationConstants.PropertyName] = poisonCorrelationId
            });

            _logger.LogError(
                ex,
                "Failed to parse JSON payload [DeliveryTag: {DeliveryTag}, EventId: {EventId}, CorrelationId: {CorrelationId}]. Forwarding directly to DLQ '{DlQueueName}' without retries.",
                deliveryTag,
                eventId?.ToString() ?? "N/A",
                poisonCorrelationId,
                _options.DeadLetterQueueName);

            await channel.BasicNackAsync(deliveryTag, multiple: false, requeue: false, cancellationToken);
            return;
        }

        var resolvedCorrelationId = !string.IsNullOrWhiteSpace(correlationId)
            ? correlationId
            : Guid.NewGuid().ToString();

        if (_correlationContextAccessor != null)
        {
            _correlationContextAccessor.CorrelationId = resolvedCorrelationId;
        }

        using var correlationLogScope = _logger.BeginScope(new Dictionary<string, object>
        {
            [CorrelationConstants.PropertyName] = resolvedCorrelationId
        });

        var resolvedEventId = eventId?.ToString() ?? "N/A";
        var resolvedOrderId = orderId.HasValue ? orderId.Value.ToString() : "N/A";

        // 2. Reject unidentifiable or unsupported events immediately to DLQ
        if (string.IsNullOrWhiteSpace(eventType) || !IsSupportedEventType(eventType))
        {
            _logger.LogWarning(
                "Unsupported or unidentifiable EventType '{EventType}' [DeliveryTag: {DeliveryTag}, EventId: {EventId}, CorrelationId: {CorrelationId}]. Forwarding directly to DLQ '{DlQueueName}' without retries.",
                eventType ?? "Unknown",
                deliveryTag,
                resolvedEventId,
                resolvedCorrelationId,
                _options.DeadLetterQueueName);

            await channel.BasicNackAsync(deliveryTag, multiple: false, requeue: false, cancellationToken);
            return;
        }

        // 3. Reject malformed envelope data immediately to DLQ
        if (!ValidateEnvelopeData(eventType, body))
        {
            _logger.LogWarning(
                "Malformed payload (null Data) for event type '{EventType}' [DeliveryTag: {DeliveryTag}, EventId: {EventId}, CorrelationId: {CorrelationId}]. Forwarding directly to DLQ '{DlQueueName}' without retries.",
                eventType,
                deliveryTag,
                resolvedEventId,
                resolvedCorrelationId,
                _options.DeadLetterQueueName);

            await channel.BasicNackAsync(deliveryTag, multiple: false, requeue: false, cancellationToken);
            return;
        }

        // 4. Retry loop with max attempts for transient failures
        int maxAttempts = Math.Max(1, _options.MaxRetryAttempts);

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                _logger.LogInformation(
                    "Processing integration event '{EventType}' [Attempt {Attempt}/{MaxAttempts}] [EventId: {EventId}, OrderId: {OrderId}, CorrelationId: {CorrelationId}].",
                    eventType,
                    attempt,
                    maxAttempts,
                    resolvedEventId,
                    resolvedOrderId,
                    resolvedCorrelationId);

                using var scope = _serviceScopeFactory.CreateScope();
                var scopedAccessor = scope.ServiceProvider.GetService<ICorrelationContextAccessor>();
                if (scopedAccessor != null)
                {
                    scopedAccessor.CorrelationId = resolvedCorrelationId;
                }
                var notification = await DispatchEventAsync(eventType, body, scope.ServiceProvider, cancellationToken);

                if (notification == null)
                {
                    _logger.LogInformation(
                        "Integration event '{EventType}' [EventId: {EventId}, OrderId: {OrderId}, CorrelationId: {CorrelationId}] was already processed. Acknowledging message without duplicate notification.",
                        eventType,
                        resolvedEventId,
                        resolvedOrderId,
                        resolvedCorrelationId);
                }
                else
                {
                    _logger.LogInformation(
                        "Notification '{NotificationId}' created for order '{OrderId}' [EventId: {EventId}, CorrelationId: {CorrelationId}].",
                        notification.Id,
                        resolvedOrderId,
                        resolvedEventId,
                        resolvedCorrelationId);
                }

                await channel.BasicAckAsync(deliveryTag, multiple: false, cancellationToken);
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts && !cancellationToken.IsCancellationRequested)
            {
                var delayMs = (int)(_options.InitialRetryDelayMs * Math.Pow(2, attempt - 1));
                _logger.LogWarning(
                    ex,
                    "Transient error on attempt {Attempt}/{MaxAttempts} processing event '{EventType}' [EventId: {EventId}, OrderId: {OrderId}, CorrelationId: {CorrelationId}]. Retrying in {DelayMs}ms...",
                    attempt,
                    maxAttempts,
                    eventType,
                    resolvedEventId,
                    resolvedOrderId,
                    resolvedCorrelationId,
                    delayMs);

                if (delayMs > 0)
                {
                    await Task.Delay(delayMs, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Exhausted all {MaxAttempts} retry attempts for event '{EventType}' [DeliveryTag: {DeliveryTag}, EventId: {EventId}, OrderId: {OrderId}, CorrelationId: {CorrelationId}]. Forwarding to DLQ '{DlQueueName}'.",
                    maxAttempts,
                    eventType,
                    deliveryTag,
                    resolvedEventId,
                    resolvedOrderId,
                    resolvedCorrelationId,
                    _options.DeadLetterQueueName);

                await channel.BasicNackAsync(deliveryTag, multiple: false, requeue: false, cancellationToken);
                return;
            }
        }
    }

    private static bool IsSupportedEventType(string eventType)
    {
        return eventType is "OrderCreated" or "OrderStatusChanged" or "OrderCompleted" or "OrderCancelled";
    }

    private static bool ValidateEnvelopeData(string eventType, ReadOnlyMemory<byte> body)
    {
        return eventType switch
        {
            "OrderCreated" => JsonSerializer.Deserialize<EventEnvelope<OrderCreatedIntegrationEvent>>(body.Span, SerializerOptions)?.Data != null,
            "OrderStatusChanged" => JsonSerializer.Deserialize<EventEnvelope<OrderStatusChangedIntegrationEvent>>(body.Span, SerializerOptions)?.Data != null,
            "OrderCompleted" => JsonSerializer.Deserialize<EventEnvelope<OrderCompletedIntegrationEvent>>(body.Span, SerializerOptions)?.Data != null,
            "OrderCancelled" => JsonSerializer.Deserialize<EventEnvelope<OrderCancelledIntegrationEvent>>(body.Span, SerializerOptions)?.Data != null,
            _ => false
        };
    }

    private static async Task<Notification?> DispatchEventAsync(
        string eventType,
        ReadOnlyMemory<byte> body,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        return eventType switch
        {
            "OrderCreated" => await serviceProvider
                .GetRequiredService<ProcessOrderCreatedEventUseCase>()
                .ExecuteAsync(JsonSerializer.Deserialize<EventEnvelope<OrderCreatedIntegrationEvent>>(body.Span, SerializerOptions)!, cancellationToken),

            "OrderStatusChanged" => await serviceProvider
                .GetRequiredService<ProcessOrderStatusChangedEventUseCase>()
                .ExecuteAsync(JsonSerializer.Deserialize<EventEnvelope<OrderStatusChangedIntegrationEvent>>(body.Span, SerializerOptions)!, cancellationToken),

            "OrderCompleted" => await serviceProvider
                .GetRequiredService<ProcessOrderCompletedEventUseCase>()
                .ExecuteAsync(JsonSerializer.Deserialize<EventEnvelope<OrderCompletedIntegrationEvent>>(body.Span, SerializerOptions)!, cancellationToken),

            "OrderCancelled" => await serviceProvider
                .GetRequiredService<ProcessOrderCancelledEventUseCase>()
                .ExecuteAsync(JsonSerializer.Deserialize<EventEnvelope<OrderCancelledIntegrationEvent>>(body.Span, SerializerOptions)!, cancellationToken),

            _ => throw new InvalidOperationException($"Unsupported event type '{eventType}'.")
        };
    }
}
