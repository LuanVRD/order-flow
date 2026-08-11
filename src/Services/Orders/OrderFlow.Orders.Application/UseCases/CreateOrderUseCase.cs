using FluentValidation;
using OrderFlow.Messaging.Contracts.Events;
using OrderFlow.Orders.Application.DTOs;
using OrderFlow.Orders.Application.Interfaces;
using OrderFlow.Orders.Domain.Entities;

namespace OrderFlow.Orders.Application.UseCases;

public class CreateOrderUseCase
{
    private readonly IOrderRepository _repository;
    private readonly IEventPublisher _eventPublisher;
    private readonly IValidator<CreateOrderCommand> _validator;

    public CreateOrderUseCase(
        IOrderRepository repository,
        IEventPublisher eventPublisher,
        IValidator<CreateOrderCommand> validator)
    {
        _repository = repository;
        _eventPublisher = eventPublisher;
        _validator = validator;
    }

    public async Task<OrderResponse> ExecuteAsync(CreateOrderCommand command, CancellationToken cancellationToken = default)
    {
        await _validator.ValidateAndThrowAsync(command, cancellationToken);

        var order = new Order(command.CustomerName, command.CustomerEmail, command.TotalAmount);

        await _repository.AddAsync(order, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        var integrationEvent = new EventEnvelope<OrderCreatedEvent>(
            Guid.NewGuid(),
            "OrderCreated",
            DateTimeOffset.UtcNow,
            1,
            new OrderCreatedEvent(
                order.Id,
                order.CustomerName,
                order.CustomerEmail,
                order.TotalAmount,
                order.Status.ToString(),
                order.CreatedAt
            )
        );

        await _eventPublisher.PublishAsync(integrationEvent, "order.created", cancellationToken);
        order.ClearDomainEvents();

        return OrderResponse.FromEntity(order);
    }
}
