namespace OrderFlow.Orders.Domain.Events;

public record OrderCompletedDomainEvent(
    Guid OrderId,
    DateTimeOffset OccurredOn
) : IDomainEvent;
