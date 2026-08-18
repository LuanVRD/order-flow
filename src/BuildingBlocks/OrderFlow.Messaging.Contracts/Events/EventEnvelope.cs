namespace OrderFlow.Messaging.Contracts.Events;

public record EventEnvelope<T>(
    Guid EventId,
    string EventType,
    DateTimeOffset OccurredAt,
    int Version,
    string? CorrelationId,
    T Data
) where T : class
{
    public static EventEnvelope<T> Create(
        string eventType,
        T data,
        string? correlationId = null,
        int version = 1,
        Guid? eventId = null,
        DateTimeOffset? occurredAt = null)
    {
        return new EventEnvelope<T>(
            eventId ?? Guid.NewGuid(),
            eventType,
            occurredAt ?? DateTimeOffset.UtcNow,
            version,
            correlationId,
            data
        );
    }
}

