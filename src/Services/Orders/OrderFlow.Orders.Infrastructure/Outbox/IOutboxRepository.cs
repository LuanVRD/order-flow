using OrderFlow.Orders.Domain.Entities;

namespace OrderFlow.Orders.Infrastructure.Outbox;

public interface IOutboxRepository
{
    Task<IReadOnlyList<OutboxMessage>> FetchAndLockPendingMessagesAsync(
        int batchSize,
        string lockId,
        TimeSpan lockDuration,
        CancellationToken cancellationToken = default);

    Task MarkAsProcessedAsync(
        Guid messageId,
        CancellationToken cancellationToken = default);

    Task RecordFailureAsync(
        Guid messageId,
        string errorMessage,
        TimeSpan retryDelay,
        CancellationToken cancellationToken = default);

    Task<int> PurgeProcessedMessagesAsync(
        DateTimeOffset olderThan,
        CancellationToken cancellationToken = default);
}
