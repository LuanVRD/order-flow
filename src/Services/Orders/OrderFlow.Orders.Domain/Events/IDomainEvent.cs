namespace OrderFlow.Orders.Domain.Events;

public interface IDomainEvent
{
    DateTimeOffset OccurredOn { get; }
}
