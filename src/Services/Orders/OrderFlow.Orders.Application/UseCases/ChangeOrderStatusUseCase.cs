using FluentValidation;
using OrderFlow.Orders.Application.DTOs;
using OrderFlow.Orders.Application.Exceptions;
using OrderFlow.Orders.Application.Interfaces;
using OrderFlow.Orders.Domain.Entities;

namespace OrderFlow.Orders.Application.UseCases;

public class ChangeOrderStatusUseCase
{
    private readonly IOrderRepository _repository;
    private readonly IValidator<ChangeOrderStatusCommand> _validator;

    public ChangeOrderStatusUseCase(
        IOrderRepository repository,
        IValidator<ChangeOrderStatusCommand> validator)
    {
        _repository = repository;
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

        order.ChangeStatus(command.NewStatus);

        await _repository.SaveChangesAsync(cancellationToken);

        return OrderResponse.FromEntity(order);
    }
}
