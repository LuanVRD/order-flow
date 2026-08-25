using OrderFlow.Messaging.Contracts.Events;
using OrderFlow.Notifications.Application.Interfaces;
using OrderFlow.Notifications.Domain.Entities;
using OrderFlow.Notifications.Domain.Enums;

namespace OrderFlow.Notifications.Application.UseCases;

public class ProcessOrderCreatedEventUseCase
{
    private readonly INotificationRepository _notificationRepository;
    private readonly IProcessedMessageRepository _processedMessageRepository;

    public ProcessOrderCreatedEventUseCase(
        INotificationRepository notificationRepository,
        IProcessedMessageRepository processedMessageRepository)
    {
        _notificationRepository = notificationRepository;
        _processedMessageRepository = processedMessageRepository;
    }

    public async Task<Notification?> ExecuteAsync(
        EventEnvelope<OrderCreatedIntegrationEvent> envelope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(envelope.Data);

        if (await _processedMessageRepository.ExistsAsync(envelope.EventId, cancellationToken))
        {
            return null;
        }

        var data = envelope.Data;
        var formattedAmount = data.TotalAmount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
        var message = $"Pedido {data.OrderId} criado com sucesso para o cliente {data.CustomerName}. Valor: R$ {formattedAmount}. Status: {data.Status}.";

        var notification = new Notification(
            orderId: data.OrderId,
            type: NotificationType.OrderCreated,
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
