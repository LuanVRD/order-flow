using OrderFlow.Orders.Domain.Enums;

namespace OrderFlow.Orders.Domain.Events;

public record OrderCancelledDomainEvent(
    Guid OrderId,
    OrderStatus PreviousStatus,
    DateTimeOffset OccurredOn
) : IDomainEvent;
