using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderFlow.Messaging.Contracts.Events;
using OrderFlow.Notifications.Application.UseCases;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OrderFlow.Notifications.Infrastructure.Messaging;

public class OrderEventsConsumer : IOrderEventsConsumer
{
    private readonly IRabbitMqConnection _connection;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<OrderEventsConsumer> _logger;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public OrderEventsConsumer(
        IRabbitMqConnection connection,
        IServiceScopeFactory serviceScopeFactory,
        IOptions<RabbitMqOptions> options,
        ILogger<OrderEventsConsumer> logger)
    {
        _connection = connection;
        _serviceScopeFactory = serviceScopeFactory;
        _options = options.Value;
        _logger = logger;
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
            "Starting consumption on queue '{QueueName}' with PrefetchCount {PrefetchCount}...",
            _options.QueueName,
            _options.PrefetchCount);

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

        try
        {
            if (string.IsNullOrWhiteSpace(eventType))
            {
                using var jsonDoc = JsonDocument.Parse(body);
                if (jsonDoc.RootElement.TryGetProperty("eventType", out var eventTypeProp) ||
                    jsonDoc.RootElement.TryGetProperty("EventType", out eventTypeProp))
                {
                    eventType = eventTypeProp.GetString();
                }
            }

            if (string.IsNullOrWhiteSpace(eventType))
            {
                _logger.LogWarning("Message received with unidentifiable EventType. Acknowledging to avoid blocking queue.");
                await channel.BasicAckAsync(deliveryTag, multiple: false, cancellationToken);
                return;
            }

            using var scope = _serviceScopeFactory.CreateScope();

            switch (eventType)
            {
                case "OrderCreated":
                {
                    var envelope = JsonSerializer.Deserialize<EventEnvelope<OrderCreatedIntegrationEvent>>(body.Span, SerializerOptions);
                    if (envelope?.Data == null)
                    {
                        _logger.LogWarning("Malformed payload for event type 'OrderCreated'. Acknowledging to avoid poisoning queue.");
                        await channel.BasicAckAsync(deliveryTag, multiple: false, cancellationToken);
                        return;
                    }

                    _logger.LogInformation(
                        "Processing integration event '{EventType}' [EventId: {EventId}, OrderId: {OrderId}, CorrelationId: {CorrelationId}].",
                        envelope.EventType,
                        envelope.EventId,
                        envelope.Data.OrderId,
                        envelope.CorrelationId ?? "N/A");

                    var useCase = scope.ServiceProvider.GetRequiredService<ProcessOrderCreatedEventUseCase>();
                    var result = await useCase.ExecuteAsync(envelope, cancellationToken);

                    if (result == null)
                    {
                        _logger.LogInformation(
                            "Integration event '{EventType}' [EventId: {EventId}, OrderId: {OrderId}] was already processed. Acknowledging message without duplicate notification.",
                            envelope.EventType,
                            envelope.EventId,
                            envelope.Data.OrderId);
                    }
                    else
                    {
                        _logger.LogInformation(
                            "Notification '{NotificationId}' created for order '{OrderId}' [EventId: {EventId}, CorrelationId: {CorrelationId}].",
                            result.Id,
                            envelope.Data.OrderId,
                            envelope.EventId,
                            envelope.CorrelationId ?? "N/A");
                    }

                    await channel.BasicAckAsync(deliveryTag, multiple: false, cancellationToken);
                    break;
                }

                case "OrderStatusChanged":
                {
                    var envelope = JsonSerializer.Deserialize<EventEnvelope<OrderStatusChangedIntegrationEvent>>(body.Span, SerializerOptions);
                    if (envelope?.Data == null)
                    {
                        _logger.LogWarning("Malformed payload for event type 'OrderStatusChanged'. Acknowledging to avoid poisoning queue.");
                        await channel.BasicAckAsync(deliveryTag, multiple: false, cancellationToken);
                        return;
                    }

                    _logger.LogInformation(
                        "Processing integration event '{EventType}' [EventId: {EventId}, OrderId: {OrderId}, CorrelationId: {CorrelationId}].",
                        envelope.EventType,
                        envelope.EventId,
                        envelope.Data.OrderId,
                        envelope.CorrelationId ?? "N/A");

                    var useCase = scope.ServiceProvider.GetRequiredService<ProcessOrderStatusChangedEventUseCase>();
                    var result = await useCase.ExecuteAsync(envelope, cancellationToken);

                    if (result == null)
                    {
                        _logger.LogInformation(
                            "Integration event '{EventType}' [EventId: {EventId}, OrderId: {OrderId}] was already processed. Acknowledging message without duplicate notification.",
                            envelope.EventType,
                            envelope.EventId,
                            envelope.Data.OrderId);
                    }
                    else
                    {
                        _logger.LogInformation(
                            "Notification '{NotificationId}' created for order '{OrderId}' [EventId: {EventId}, CorrelationId: {CorrelationId}].",
                            result.Id,
                            envelope.Data.OrderId,
                            envelope.EventId,
                            envelope.CorrelationId ?? "N/A");
                    }

                    await channel.BasicAckAsync(deliveryTag, multiple: false, cancellationToken);
                    break;
                }

                case "OrderCompleted":
                {
                    var envelope = JsonSerializer.Deserialize<EventEnvelope<OrderCompletedIntegrationEvent>>(body.Span, SerializerOptions);
                    if (envelope?.Data == null)
                    {
                        _logger.LogWarning("Malformed payload for event type 'OrderCompleted'. Acknowledging to avoid poisoning queue.");
                        await channel.BasicAckAsync(deliveryTag, multiple: false, cancellationToken);
                        return;
                    }

                    _logger.LogInformation(
                        "Processing integration event '{EventType}' [EventId: {EventId}, OrderId: {OrderId}, CorrelationId: {CorrelationId}].",
                        envelope.EventType,
                        envelope.EventId,
                        envelope.Data.OrderId,
                        envelope.CorrelationId ?? "N/A");

                    var useCase = scope.ServiceProvider.GetRequiredService<ProcessOrderCompletedEventUseCase>();
                    var result = await useCase.ExecuteAsync(envelope, cancellationToken);

                    if (result == null)
                    {
                        _logger.LogInformation(
                            "Integration event '{EventType}' [EventId: {EventId}, OrderId: {OrderId}] was already processed. Acknowledging message without duplicate notification.",
                            envelope.EventType,
                            envelope.EventId,
                            envelope.Data.OrderId);
                    }
                    else
                    {
                        _logger.LogInformation(
                            "Notification '{NotificationId}' created for order '{OrderId}' [EventId: {EventId}, CorrelationId: {CorrelationId}].",
                            result.Id,
                            envelope.Data.OrderId,
                            envelope.EventId,
                            envelope.CorrelationId ?? "N/A");
                    }

                    await channel.BasicAckAsync(deliveryTag, multiple: false, cancellationToken);
                    break;
                }

                case "OrderCancelled":
                {
                    var envelope = JsonSerializer.Deserialize<EventEnvelope<OrderCancelledIntegrationEvent>>(body.Span, SerializerOptions);
                    if (envelope?.Data == null)
                    {
                        _logger.LogWarning("Malformed payload for event type 'OrderCancelled'. Acknowledging to avoid poisoning queue.");
                        await channel.BasicAckAsync(deliveryTag, multiple: false, cancellationToken);
                        return;
                    }

                    _logger.LogInformation(
                        "Processing integration event '{EventType}' [EventId: {EventId}, OrderId: {OrderId}, CorrelationId: {CorrelationId}].",
                        envelope.EventType,
                        envelope.EventId,
                        envelope.Data.OrderId,
                        envelope.CorrelationId ?? "N/A");

                    var useCase = scope.ServiceProvider.GetRequiredService<ProcessOrderCancelledEventUseCase>();
                    var result = await useCase.ExecuteAsync(envelope, cancellationToken);

                    if (result == null)
                    {
                        _logger.LogInformation(
                            "Integration event '{EventType}' [EventId: {EventId}, OrderId: {OrderId}] was already processed. Acknowledging message without duplicate notification.",
                            envelope.EventType,
                            envelope.EventId,
                            envelope.Data.OrderId);
                    }
                    else
                    {
                        _logger.LogInformation(
                            "Notification '{NotificationId}' created for order '{OrderId}' [EventId: {EventId}, CorrelationId: {CorrelationId}].",
                            result.Id,
                            envelope.Data.OrderId,
                            envelope.EventId,
                            envelope.CorrelationId ?? "N/A");
                    }

                    await channel.BasicAckAsync(deliveryTag, multiple: false, cancellationToken);
                    break;
                }

                default:
                    _logger.LogWarning(
                        "Unsupported event type '{EventType}'. Acknowledging to discard.",
                        eventType);
                    await channel.BasicAckAsync(deliveryTag, multiple: false, cancellationToken);
                    break;
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize JSON message payload for delivery tag {DeliveryTag}. Acknowledging to discard invalid message.", deliveryTag);
            await channel.BasicAckAsync(deliveryTag, multiple: false, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unhandled error occurred while processing message [DeliveryTag: {DeliveryTag}, EventType: {EventType}]. Requeuing message.",
                deliveryTag,
                eventType ?? "Unknown");

            await channel.BasicNackAsync(deliveryTag, multiple: false, requeue: true, cancellationToken);
        }
    }
}
