using OrderFlow.Orders.Domain.Enums;

namespace OrderFlow.Orders.Domain.Events;

public record OrderStatusChangedDomainEvent(
    Guid OrderId,
    OrderStatus PreviousStatus,
    OrderStatus NewStatus,
    DateTimeOffset OccurredOn
) : IDomainEvent;
