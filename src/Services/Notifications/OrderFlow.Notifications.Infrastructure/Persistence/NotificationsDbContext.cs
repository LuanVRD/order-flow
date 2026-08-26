using Microsoft.EntityFrameworkCore;
using OrderFlow.Notifications.Domain.Entities;

namespace OrderFlow.Notifications.Infrastructure.Persistence;

public class NotificationsDbContext : DbContext
{
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();

    public NotificationsDbContext(DbContextOptions<NotificationsDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NotificationsDbContext).Assembly);
    }
}
