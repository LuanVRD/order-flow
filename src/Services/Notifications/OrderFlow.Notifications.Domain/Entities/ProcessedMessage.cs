using OrderFlow.Notifications.Domain.Exceptions;

namespace OrderFlow.Notifications.Domain.Entities;

public class ProcessedMessage
{
    public Guid EventId { get; private set; }
    public string? EventType { get; private set; }
    public DateTimeOffset ProcessedAt { get; private set; }

    // Construtor privado para ORMs/serialização
    private ProcessedMessage()
    {
    }

    public ProcessedMessage(Guid eventId, string? eventType = null, DateTimeOffset? processedAt = null)
    {
        if (eventId == Guid.Empty)
        {
            throw new DomainException("EventId cannot be empty.");
        }

        EventId = eventId;
        EventType = eventType;
        ProcessedAt = processedAt ?? DateTimeOffset.UtcNow;
    }
}
