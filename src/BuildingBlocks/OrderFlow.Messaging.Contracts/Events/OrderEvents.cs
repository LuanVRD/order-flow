namespace OrderFlow.Messaging.Contracts.Events;

public record OrderCreatedIntegrationEvent(
    Guid OrderId,
    string CustomerName,
    string CustomerEmail,
    decimal TotalAmount,
    string Status,
    DateTimeOffset CreatedAt
);

public record OrderStatusChangedIntegrationEvent(
    Guid OrderId,
    string PreviousStatus,
    string NewStatus,
    DateTimeOffset ChangedAt
);

public record OrderCompletedIntegrationEvent(
    Guid OrderId,
    DateTimeOffset CompletedAt
);

public record OrderCancelledIntegrationEvent(
    Guid OrderId,
    string PreviousStatus,
    DateTimeOffset CancelledAt,
    string? Reason = null
);

