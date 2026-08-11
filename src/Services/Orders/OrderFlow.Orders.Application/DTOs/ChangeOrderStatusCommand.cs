using OrderFlow.Orders.Domain.Enums;

namespace OrderFlow.Orders.Application.DTOs;

public record ChangeOrderStatusCommand(
    Guid Id,
    OrderStatus NewStatus
);
