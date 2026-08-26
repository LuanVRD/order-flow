using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OrderFlow.Notifications.Domain.Entities;
using OrderFlow.Notifications.Domain.Enums;
using OrderFlow.Notifications.Infrastructure.Persistence;
using OrderFlow.Notifications.Infrastructure.Persistence.Repositories;
using Xunit;

namespace OrderFlow.Notifications.Tests.Infrastructure.Persistence;

public class NotificationRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<NotificationsDbContext> _dbContextOptions;

    public NotificationRepositoryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _dbContextOptions = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = new NotificationsDbContext(_dbContextOptions);
        context.Database.EnsureCreated();
    }

    [Fact]
    public async Task AddAsync_ShouldPersistNotificationInDatabase()
    {
        // Arrange
        await using var context = new NotificationsDbContext(_dbContextOptions);
        var repository = new NotificationRepository(context);
        var orderId = Guid.NewGuid();
        var notification = new Notification(orderId, NotificationType.OrderCreated, "Pedido criado com sucesso.");

        // Act
        await repository.AddAsync(notification);

        // Assert
        await using var verifyContext = new NotificationsDbContext(_dbContextOptions);
        var persistedNotification = await verifyContext.Notifications.FirstOrDefaultAsync(n => n.Id == notification.Id);

        Assert.NotNull(persistedNotification);
        Assert.Equal(notification.Id, persistedNotification.Id);
        Assert.Equal(orderId, persistedNotification.OrderId);
        Assert.Equal(NotificationType.OrderCreated, persistedNotification.Type);
        Assert.Equal("Pedido criado com sucesso.", persistedNotification.Message);
        Assert.Equal(notification.CreatedAt, persistedNotification.CreatedAt);
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnNotification_WhenExists()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var notification = new Notification(orderId, NotificationType.OrderStatusChanged, "Status alterado para Processando.");

        await using (var seedContext = new NotificationsDbContext(_dbContextOptions))
        {
            seedContext.Notifications.Add(notification);
            await seedContext.SaveChangesAsync();
        }

        await using var context = new NotificationsDbContext(_dbContextOptions);
        var repository = new NotificationRepository(context);

        // Act
        var result = await repository.GetByIdAsync(notification.Id);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(notification.Id, result.Id);
        Assert.Equal(orderId, result.OrderId);
        Assert.Equal(NotificationType.OrderStatusChanged, result.Type);
        Assert.Equal("Status alterado para Processando.", result.Message);
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnNull_WhenNotificationDoesNotExist()
    {
        // Arrange
        await using var context = new NotificationsDbContext(_dbContextOptions);
        var repository = new NotificationRepository(context);

        // Act
        var result = await repository.GetByIdAsync(Guid.NewGuid());

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetByOrderIdAsync_ShouldReturnNotificationsForGivenOrderId()
    {
        // Arrange
        var targetOrderId = Guid.NewGuid();
        var otherOrderId = Guid.NewGuid();

        var notification1 = new Notification(targetOrderId, NotificationType.OrderCreated, "Mensagem 1", createdAt: DateTimeOffset.UtcNow.AddMinutes(-5));
        var notification2 = new Notification(targetOrderId, NotificationType.OrderStatusChanged, "Mensagem 2", createdAt: DateTimeOffset.UtcNow);
        var notification3 = new Notification(otherOrderId, NotificationType.OrderCreated, "Mensagem outro pedido", createdAt: DateTimeOffset.UtcNow);

        await using (var seedContext = new NotificationsDbContext(_dbContextOptions))
        {
            seedContext.Notifications.AddRange(notification1, notification2, notification3);
            await seedContext.SaveChangesAsync();
        }

        await using var context = new NotificationsDbContext(_dbContextOptions);
        var repository = new NotificationRepository(context);

        // Act
        var results = await repository.GetByOrderIdAsync(targetOrderId);

        // Assert
        Assert.NotNull(results);
        Assert.Equal(2, results.Count);
        Assert.All(results, n => Assert.Equal(targetOrderId, n.OrderId));
        Assert.Contains(results, n => n.Id == notification1.Id);
        Assert.Contains(results, n => n.Id == notification2.Id);
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
