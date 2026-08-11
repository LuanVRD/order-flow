namespace OrderFlow.Messaging.Contracts.Events;

public record OrderCreatedEvent(
    Guid OrderId,
    string CustomerName,
    string CustomerEmail,
    decimal TotalAmount,
    string Status,
    DateTimeOffset CreatedAt
);

public record OrderStatusChangedEvent(
    Guid OrderId,
    string PreviousStatus,
    string NewStatus,
    DateTimeOffset ChangedAt
);

public record OrderCompletedEvent(
    Guid OrderId,
    DateTimeOffset CompletedAt
);

public record OrderCancelledEvent(
    Guid OrderId,
    string PreviousStatus,
    DateTimeOffset CancelledAt
);
