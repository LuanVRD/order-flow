using OrderFlow.Notifications.Domain.Entities;

namespace OrderFlow.Notifications.Application.Interfaces;

public interface INotificationRepository
{
    Task AddAsync(Notification notification, CancellationToken cancellationToken = default);
    Task<Notification?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<Notification>> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default);
}
