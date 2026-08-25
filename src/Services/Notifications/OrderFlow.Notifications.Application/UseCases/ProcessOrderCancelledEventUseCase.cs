using OrderFlow.Messaging.Contracts.Events;
using OrderFlow.Notifications.Application.Interfaces;
using OrderFlow.Notifications.Domain.Entities;
using OrderFlow.Notifications.Domain.Enums;

namespace OrderFlow.Notifications.Application.UseCases;

public class ProcessOrderCancelledEventUseCase
{
    private readonly INotificationRepository _notificationRepository;
    private readonly IProcessedMessageRepository _processedMessageRepository;

    public ProcessOrderCancelledEventUseCase(
        INotificationRepository notificationRepository,
        IProcessedMessageRepository processedMessageRepository)
    {
        _notificationRepository = notificationRepository;
        _processedMessageRepository = processedMessageRepository;
    }

    public async Task<Notification?> ExecuteAsync(
        EventEnvelope<OrderCancelledIntegrationEvent> envelope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(envelope.Data);

        if (await _processedMessageRepository.ExistsAsync(envelope.EventId, cancellationToken))
        {
            return null;
        }

        var data = envelope.Data;
        var message = string.IsNullOrWhiteSpace(data.Reason)
            ? $"Pedido {data.OrderId} foi cancelado."
            : $"Pedido {data.OrderId} foi cancelado. Motivo: {data.Reason.Trim()}.";

        var notification = new Notification(
            orderId: data.OrderId,
            type: NotificationType.OrderCancelled,
            message: message,
            createdAt: envelope.OccurredAt
        );

        var processedMessage = new ProcessedMessage(
            eventId: envelope.EventId,
            eventType: envelope.EventType
        );

        await _notificationRepository.AddAsync(notification, cancellationToken);
        await _processedMessageRepository.AddAsync(processedMessage, cancellationToken);

        return notification;
    }
}
