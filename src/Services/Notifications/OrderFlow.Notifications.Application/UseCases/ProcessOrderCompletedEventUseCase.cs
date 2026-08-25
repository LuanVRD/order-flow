using OrderFlow.Messaging.Contracts.Events;
using OrderFlow.Notifications.Application.Interfaces;
using OrderFlow.Notifications.Domain.Entities;
using OrderFlow.Notifications.Domain.Enums;

namespace OrderFlow.Notifications.Application.UseCases;

public class ProcessOrderCompletedEventUseCase
{
    private readonly INotificationRepository _notificationRepository;
    private readonly IProcessedMessageRepository _processedMessageRepository;

    public ProcessOrderCompletedEventUseCase(
        INotificationRepository notificationRepository,
        IProcessedMessageRepository processedMessageRepository)
    {
        _notificationRepository = notificationRepository;
        _processedMessageRepository = processedMessageRepository;
    }

    public async Task<Notification?> ExecuteAsync(
        EventEnvelope<OrderCompletedIntegrationEvent> envelope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(envelope.Data);

        if (await _processedMessageRepository.ExistsAsync(envelope.EventId, cancellationToken))
        {
            return null;
        }

        var data = envelope.Data;
        var message = $"Pedido {data.OrderId} foi concluído com sucesso.";

        var notification = new Notification(
            orderId: data.OrderId,
            type: NotificationType.OrderCompleted,
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
