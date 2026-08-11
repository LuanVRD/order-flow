using OrderFlow.Orders.Application.DTOs;
using OrderFlow.Orders.Application.Exceptions;
using OrderFlow.Orders.Application.Interfaces;
using OrderFlow.Orders.Domain.Entities;

namespace OrderFlow.Orders.Application.UseCases;

public class GetOrderByIdUseCase
{
    private readonly IOrderRepository _repository;

    public GetOrderByIdUseCase(IOrderRepository repository)
    {
        _repository = repository;
    }

    public async Task<OrderResponse> ExecuteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var order = await _repository.GetByIdAsync(id, cancellationToken);
        if (order == null)
        {
            throw new NotFoundException(nameof(Order), id);
        }

        return OrderResponse.FromEntity(order);
    }
}
