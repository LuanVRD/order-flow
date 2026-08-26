using Microsoft.EntityFrameworkCore;
using OrderFlow.Notifications.Application.Interfaces;
using OrderFlow.Notifications.Domain.Entities;

namespace OrderFlow.Notifications.Infrastructure.Persistence.Repositories;

public class NotificationRepository : INotificationRepository
{
    private readonly NotificationsDbContext _context;

    public NotificationRepository(NotificationsDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task AddAsync(Notification notification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);

        await _context.Notifications.AddAsync(notification, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<Notification?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Notifications
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyCollection<Notification>> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var notifications = await _context.Notifications
            .AsNoTracking()
            .Where(n => n.OrderId == orderId)
            .ToListAsync(cancellationToken);

        return notifications
            .OrderByDescending(n => n.CreatedAt)
            .ToList();
    }
}
