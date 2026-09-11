using OrderFlow.Notifications.Application.Interfaces;

namespace OrderFlow.Notifications.Infrastructure.Persistence;

public class UnitOfWork : IUnitOfWork
{
    private readonly NotificationsDbContext _context;

    public UnitOfWork(NotificationsDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<int> CommitAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SaveChangesAsync(cancellationToken);
    }
}
