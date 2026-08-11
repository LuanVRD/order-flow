using FluentValidation;
using OrderFlow.Messaging.Contracts.Events;
using OrderFlow.Orders.Application.DTOs;
using OrderFlow.Orders.Application.Exceptions;
using OrderFlow.Orders.Application.Interfaces;
using OrderFlow.Orders.Domain.Entities;

namespace OrderFlow.Orders.Application.UseCases;

public class CancelOrderUseCase
{
    private readonly IOrderRepository _repository;
    private readonly IEventPublisher _eventPublisher;
    private readonly IValidator<CancelOrderCommand> _validator;

    public CancelOrderUseCase(
        IOrderRepository repository,
        IEventPublisher eventPublisher,
        IValidator<CancelOrderCommand> validator)
    {
        _repository = repository;
        _eventPublisher = eventPublisher;
        _validator = validator;
    }

    public async Task<OrderResponse> ExecuteAsync(CancelOrderCommand command, CancellationToken cancellationToken = default)
    {
        await _validator.ValidateAndThrowAsync(command, cancellationToken);

        var order = await _repository.GetByIdAsync(command.Id, cancellationToken);
        if (order == null)
        {
            throw new NotFoundException(nameof(Order), command.Id);
        }

        var previousStatus = order.Status;
        order.Cancel();

        await _repository.SaveChangesAsync(cancellationToken);

        var statusChangedEvent = new EventEnvelope<OrderStatusChangedEvent>(
            Guid.NewGuid(),
            "OrderStatusChanged",
            DateTimeOffset.UtcNow,
            1,
            new OrderStatusChangedEvent(
                order.Id,
                previousStatus.ToString(),
                order.Status.ToString(),
                order.UpdatedAt ?? DateTimeOffset.UtcNow
            )
        );
        await _eventPublisher.PublishAsync(statusChangedEvent, "order.status.changed", cancellationToken);

        var cancelledEvent = new EventEnvelope<OrderCancelledEvent>(
            Guid.NewGuid(),
            "OrderCancelled",
            DateTimeOffset.UtcNow,
            1,
            new OrderCancelledEvent(order.Id, previousStatus.ToString(), order.UpdatedAt ?? DateTimeOffset.UtcNow)
        );
        await _eventPublisher.PublishAsync(cancelledEvent, "order.cancelled", cancellationToken);

        order.ClearDomainEvents();

        return OrderResponse.FromEntity(order);
    }
}
