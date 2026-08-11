namespace OrderFlow.Orders.Application.DTOs;

public record CreateOrderCommand(
    string CustomerName,
    string CustomerEmail,
    decimal TotalAmount
);
