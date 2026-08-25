using OrderFlow.Notifications.Domain.Enums;
using OrderFlow.Notifications.Domain.Exceptions;

namespace OrderFlow.Notifications.Domain.Entities;

public class Notification
{
    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public NotificationType Type { get; private set; }
    public string Message { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    // Construtor privado para ORMs/serialização
    private Notification()
    {
    }

    public Notification(Guid orderId, NotificationType type, string message, Guid? id = null, DateTimeOffset? createdAt = null)
    {
        if (orderId == Guid.Empty)
        {
            throw new DomainException("OrderId cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            throw new DomainException("Message is required and cannot be empty.");
        }

        Id = id ?? Guid.NewGuid();
        OrderId = orderId;
        Type = type;
        Message = message.Trim();
        CreatedAt = createdAt ?? DateTimeOffset.UtcNow;
    }
}
