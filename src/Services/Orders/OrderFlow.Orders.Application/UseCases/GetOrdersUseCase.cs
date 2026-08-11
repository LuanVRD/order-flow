using OrderFlow.Orders.Application.DTOs;
using OrderFlow.Orders.Application.Interfaces;

namespace OrderFlow.Orders.Application.UseCases;

public class GetOrdersUseCase
{
    private readonly IOrderRepository _repository;

    public GetOrdersUseCase(IOrderRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyCollection<OrderResponse>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var orders = await _repository.GetAllAsync(cancellationToken);
        return orders.Select(OrderResponse.FromEntity).ToList().AsReadOnly();
    }
}
