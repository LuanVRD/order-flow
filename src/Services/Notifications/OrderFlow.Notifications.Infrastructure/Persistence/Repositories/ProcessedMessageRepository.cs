using Microsoft.EntityFrameworkCore;
using OrderFlow.Notifications.Application.Interfaces;
using OrderFlow.Notifications.Domain.Entities;

namespace OrderFlow.Notifications.Infrastructure.Persistence.Repositories;

public class ProcessedMessageRepository : IProcessedMessageRepository
{
    private readonly NotificationsDbContext _context;

    public ProcessedMessageRepository(NotificationsDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<bool> ExistsAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        return await _context.ProcessedMessages
            .AnyAsync(p => p.EventId == eventId, cancellationToken);
    }

    public async Task AddAsync(ProcessedMessage processedMessage, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(processedMessage);

        await _context.ProcessedMessages.AddAsync(processedMessage, cancellationToken);
    }
}
