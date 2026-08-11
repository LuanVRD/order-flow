using OrderFlow.Orders.Domain.Entities;
using OrderFlow.Orders.Domain.Enums;

namespace OrderFlow.Orders.Application.DTOs;

public record OrderResponse(
    Guid Id,
    string CustomerName,
    string CustomerEmail,
    OrderStatus Status,
    decimal TotalAmount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt
)
{
    public static OrderResponse FromEntity(Order order)
    {
        return new OrderResponse(
            order.Id,
            order.CustomerName,
            order.CustomerEmail,
            order.Status,
            order.TotalAmount,
            order.CreatedAt,
            order.UpdatedAt
        );
    }
}
