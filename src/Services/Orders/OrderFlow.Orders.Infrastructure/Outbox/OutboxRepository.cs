using Microsoft.EntityFrameworkCore;
using OrderFlow.Orders.Domain.Entities;
using OrderFlow.Orders.Infrastructure.Persistence;

namespace OrderFlow.Orders.Infrastructure.Outbox;

public class OutboxRepository : IOutboxRepository
{
    private readonly OrdersDbContext _dbContext;

    public OutboxRepository(OrdersDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<IReadOnlyList<OutboxMessage>> FetchAndLockPendingMessagesAsync(
        int batchSize,
        string lockId,
        TimeSpan lockDuration,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var lockExpiration = now.Add(lockDuration);

        var candidateIds = await _dbContext.OutboxMessages
            .Where(m => m.ProcessedAt == null &&
                        (m.LockedUntilUtc == null || m.LockedUntilUtc < now) &&
                        (m.NextRetryAtUtc == null || m.NextRetryAtUtc <= now))
            .OrderBy(m => m.CreatedAt)
            .Take(batchSize)
            .Select(m => m.Id)
            .ToListAsync(cancellationToken);

        if (candidateIds.Count == 0)
        {
            return Array.Empty<OutboxMessage>();
        }

        await _dbContext.OutboxMessages
            .Where(m => candidateIds.Contains(m.Id) &&
                        m.ProcessedAt == null &&
                        (m.LockedUntilUtc == null || m.LockedUntilUtc < now))
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.LockId, lockId)
                .SetProperty(m => m.LockedUntilUtc, lockExpiration),
                cancellationToken);

        return await _dbContext.OutboxMessages
            .Where(m => candidateIds.Contains(m.Id) && m.LockId == lockId && m.LockedUntilUtc == lockExpiration)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task MarkAsProcessedAsync(
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        await _dbContext.OutboxMessages
            .Where(m => m.Id == messageId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.ProcessedAt, now)
                .SetProperty(m => m.LastError, (string?)null)
                .SetProperty(m => m.LockId, (string?)null)
                .SetProperty(m => m.LockedUntilUtc, (DateTimeOffset?)null),
                cancellationToken);
    }

    public async Task RecordFailureAsync(
        Guid messageId,
        string errorMessage,
        TimeSpan retryDelay,
        CancellationToken cancellationToken = default)
    {
        var nextRetry = DateTimeOffset.UtcNow.Add(retryDelay);
        await _dbContext.OutboxMessages
            .Where(m => m.Id == messageId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.RetryCount, m => m.RetryCount + 1)
                .SetProperty(m => m.LastError, errorMessage)
                .SetProperty(m => m.NextRetryAtUtc, nextRetry)
                .SetProperty(m => m.LockId, (string?)null)
                .SetProperty(m => m.LockedUntilUtc, (DateTimeOffset?)null),
                cancellationToken);
    }

    public async Task<int> PurgeProcessedMessagesAsync(
        DateTimeOffset olderThan,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.OutboxMessages
            .Where(m => m.ProcessedAt != null && m.ProcessedAt < olderThan)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
