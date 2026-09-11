namespace OrderFlow.Orders.Domain.Entities;

public class OutboxMessage
{
    public Guid Id { get; private set; }
    public string Type { get; private set; } = string.Empty;
    public int Version { get; private set; }
    public string Payload { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public int RetryCount { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }
    public string? LastError { get; private set; }
    public DateTimeOffset? NextRetryAtUtc { get; private set; }
    public string? LockId { get; private set; }
    public DateTimeOffset? LockedUntilUtc { get; private set; }

    // Private constructor for EF Core
    private OutboxMessage()
    {
    }

    public OutboxMessage(Guid id, string type, int version, string payload, DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Outbox message Id cannot be empty.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(type))
        {
            throw new ArgumentException("Outbox message Type cannot be empty.", nameof(type));
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            throw new ArgumentException("Outbox message Payload cannot be empty.", nameof(payload));
        }

        Id = id;
        Type = type.Trim();
        Version = version > 0 ? version : 1;
        Payload = payload;
        CreatedAt = createdAt;
        RetryCount = 0;
        ProcessedAt = null;
        LastError = null;
        NextRetryAtUtc = null;
        LockId = null;
        LockedUntilUtc = null;
    }

    public void MarkAsProcessed(DateTimeOffset? processedAt = null)
    {
        ProcessedAt = processedAt ?? DateTimeOffset.UtcNow;
        LastError = null;
        LockId = null;
        LockedUntilUtc = null;
    }

    public void RecordFailure(string errorMessage, TimeSpan retryDelay)
    {
        RetryCount++;
        LastError = errorMessage;
        NextRetryAtUtc = DateTimeOffset.UtcNow.Add(retryDelay);
        LockId = null;
        LockedUntilUtc = null;
    }

    public void AcquireLock(string lockId, TimeSpan lockDuration)
    {
        LockId = lockId;
        LockedUntilUtc = DateTimeOffset.UtcNow.Add(lockDuration);
    }

    public void ReleaseLock()
    {
        LockId = null;
        LockedUntilUtc = null;
    }
}
