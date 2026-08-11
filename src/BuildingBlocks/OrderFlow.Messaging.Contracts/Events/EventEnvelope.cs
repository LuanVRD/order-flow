namespace OrderFlow.Messaging.Contracts.Events;

public record EventEnvelope<T>(
    Guid EventId,
    string EventType,
    DateTimeOffset OccurredAt,
    int Version,
    T Data
) where T : class;
