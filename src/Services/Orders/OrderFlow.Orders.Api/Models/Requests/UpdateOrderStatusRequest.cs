using OrderFlow.Orders.Domain.Enums;

namespace OrderFlow.Orders.Api.Models.Requests;

public record UpdateOrderStatusRequest(
    OrderStatus NewStatus
);
