using OrderFlow.Notifications.Domain.Entities;

namespace OrderFlow.Notifications.Application.Interfaces;

public interface IProcessedMessageRepository
{
    Task<bool> ExistsAsync(Guid eventId, CancellationToken cancellationToken = default);
    Task AddAsync(ProcessedMessage processedMessage, CancellationToken cancellationToken = default);
}
