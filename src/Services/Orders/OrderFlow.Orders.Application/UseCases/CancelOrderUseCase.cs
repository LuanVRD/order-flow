using FluentValidation;
using OrderFlow.Orders.Application.DTOs;
using OrderFlow.Orders.Application.Exceptions;
using OrderFlow.Orders.Application.Interfaces;
using OrderFlow.Orders.Domain.Entities;

namespace OrderFlow.Orders.Application.UseCases;

public class CancelOrderUseCase
{
    private readonly IOrderRepository _repository;
    private readonly IValidator<CancelOrderCommand> _validator;

    public CancelOrderUseCase(
        IOrderRepository repository,
        IValidator<CancelOrderCommand> validator)
    {
        _repository = repository;
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

        order.Cancel();

        await _repository.SaveChangesAsync(cancellationToken);

        return OrderResponse.FromEntity(order);
    }
}
