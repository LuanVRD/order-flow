using OrderFlow.Orders.Domain.Enums;

namespace OrderFlow.Orders.Domain.Events;

public record OrderCreatedDomainEvent(
    Guid OrderId,
    string CustomerName,
    string CustomerEmail,
    decimal TotalAmount,
    OrderStatus Status,
    DateTimeOffset OccurredOn
) : IDomainEvent;
