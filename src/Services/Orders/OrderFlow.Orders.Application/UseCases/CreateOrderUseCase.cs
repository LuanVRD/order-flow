using FluentValidation;
using OrderFlow.Orders.Application.DTOs;
using OrderFlow.Orders.Application.Interfaces;
using OrderFlow.Orders.Domain.Entities;

namespace OrderFlow.Orders.Application.UseCases;

public class CreateOrderUseCase
{
    private readonly IOrderRepository _repository;
    private readonly IValidator<CreateOrderCommand> _validator;

    public CreateOrderUseCase(
        IOrderRepository repository,
        IValidator<CreateOrderCommand> validator)
    {
        _repository = repository;
        _validator = validator;
    }

    public async Task<OrderResponse> ExecuteAsync(CreateOrderCommand command, CancellationToken cancellationToken = default)
    {
        await _validator.ValidateAndThrowAsync(command, cancellationToken);

        var order = new Order(command.CustomerName, command.CustomerEmail, command.TotalAmount);

        await _repository.AddAsync(order, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return OrderResponse.FromEntity(order);
    }
}
