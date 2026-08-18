using FluentValidation;
using OrderFlow.Messaging.Contracts.Events;
using OrderFlow.Orders.Application.DTOs;
using OrderFlow.Orders.Application.Exceptions;
using OrderFlow.Orders.Application.Interfaces;
using OrderFlow.Orders.Domain.Entities;
using OrderFlow.Orders.Domain.Enums;

namespace OrderFlow.Orders.Application.UseCases;

public class ChangeOrderStatusUseCase
{
    private readonly IOrderRepository _repository;
    private readonly IEventPublisher _eventPublisher;
    private readonly IValidator<ChangeOrderStatusCommand> _validator;

    public ChangeOrderStatusUseCase(
        IOrderRepository repository,
        IEventPublisher eventPublisher,
        IValidator<ChangeOrderStatusCommand> validator)
    {
        _repository = repository;
        _eventPublisher = eventPublisher;
        _validator = validator;
    }

    public async Task<OrderResponse> ExecuteAsync(ChangeOrderStatusCommand command, CancellationToken cancellationToken = default)
    {
        await _validator.ValidateAndThrowAsync(command, cancellationToken);

        var order = await _repository.GetByIdAsync(command.Id, cancellationToken);
        if (order == null)
        {
            throw new NotFoundException(nameof(Order), command.Id);
        }

        var previousStatus = order.Status;
        order.ChangeStatus(command.NewStatus);

        await _repository.SaveChangesAsync(cancellationToken);

        var statusChangedEvent = EventEnvelope<OrderStatusChangedIntegrationEvent>.Create(
            eventType: "OrderStatusChanged",
            data: new OrderStatusChangedIntegrationEvent(
                order.Id,
                previousStatus.ToString(),
                order.Status.ToString(),
                order.UpdatedAt ?? DateTimeOffset.UtcNow
            )
        );
        await _eventPublisher.PublishAsync(statusChangedEvent, "order.status.changed", cancellationToken);

        if (order.Status == OrderStatus.Completed)
        {
            var completedEvent = EventEnvelope<OrderCompletedIntegrationEvent>.Create(
                eventType: "OrderCompleted",
                data: new OrderCompletedIntegrationEvent(order.Id, order.UpdatedAt ?? DateTimeOffset.UtcNow)
            );
            await _eventPublisher.PublishAsync(completedEvent, "order.completed", cancellationToken);
        }
        else if (order.Status == OrderStatus.Cancelled)
        {
            var cancelledEvent = EventEnvelope<OrderCancelledIntegrationEvent>.Create(
                eventType: "OrderCancelled",
                data: new OrderCancelledIntegrationEvent(order.Id, previousStatus.ToString(), order.UpdatedAt ?? DateTimeOffset.UtcNow)
            );
            await _eventPublisher.PublishAsync(cancelledEvent, "order.cancelled", cancellationToken);
        }

        order.ClearDomainEvents();

        return OrderResponse.FromEntity(order);
    }
}
