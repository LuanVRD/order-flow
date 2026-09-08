using Microsoft.EntityFrameworkCore;
using OrderFlow.Notifications.Domain.Entities;
using OrderFlow.Notifications.Domain.Enums;
using OrderFlow.Notifications.Infrastructure.Persistence;
using OrderFlow.Notifications.Infrastructure.Persistence.Repositories;
using Testcontainers.PostgreSql;
using Xunit;

namespace OrderFlow.Notifications.Tests.Infrastructure.Persistence;

public class PostgreSqlNotificationPersistenceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgresContainer = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("notifications_test_db")
        .WithUsername("test_user")
        .WithPassword("test_pass")
        .Build();

    private DbContextOptions<NotificationsDbContext> _dbContextOptions = null!;

    public async Task InitializeAsync()
    {
        await _postgresContainer.StartAsync();

        _dbContextOptions = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseNpgsql(_postgresContainer.GetConnectionString())
            .Options;

        await using var context = new NotificationsDbContext(_dbContextOptions);
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _postgresContainer.DisposeAsync();
    }

    [Fact]
    public async Task NotificationRepository_AddAsync_ShouldPersistNotificationInPostgreSql()
    {
        // Arrange
        await using var context = new NotificationsDbContext(_dbContextOptions);
        var repository = new NotificationRepository(context);
        var orderId = Guid.NewGuid();
        var notification = new Notification(orderId, NotificationType.OrderCreated, $"Pedido {orderId} criado com sucesso no PostgreSQL.");

        // Act
        await repository.AddAsync(notification);
        await context.SaveChangesAsync();

        // Assert
        await using var verifyContext = new NotificationsDbContext(_dbContextOptions);
        var persisted = await verifyContext.Notifications.FirstOrDefaultAsync(n => n.Id == notification.Id);

        Assert.NotNull(persisted);
        Assert.Equal(notification.Id, persisted.Id);
        Assert.Equal(orderId, persisted.OrderId);
        Assert.Equal(NotificationType.OrderCreated, persisted.Type);
        Assert.Contains("PostgreSQL", persisted.Message);
        Assert.Equal(notification.CreatedAt.UtcDateTime, persisted.CreatedAt.UtcDateTime, TimeSpan.FromMilliseconds(10));
    }

    [Fact]
    public async Task NotificationRepository_GetByOrderIdAsync_ShouldReturnNotificationsForOrderInPostgreSql()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var n1 = new Notification(orderId, NotificationType.OrderCreated, "Criado");
        var n2 = new Notification(orderId, NotificationType.OrderStatusChanged, "Status alterado");

        await using (var seedContext = new NotificationsDbContext(_dbContextOptions))
        {
            seedContext.Notifications.AddRange(n1, n2);
            await seedContext.SaveChangesAsync();
        }

        await using var context = new NotificationsDbContext(_dbContextOptions);
        var repository = new NotificationRepository(context);

        // Act
        var results = await repository.GetByOrderIdAsync(orderId);

        // Assert
        Assert.NotNull(results);
        Assert.Equal(2, results.Count);
        Assert.Contains(results, n => n.Type == NotificationType.OrderCreated);
        Assert.Contains(results, n => n.Type == NotificationType.OrderStatusChanged);
    }

    [Fact]
    public async Task ProcessedMessageRepository_ShouldGuaranteeIdempotencyInPostgreSql()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var processedMessage = new ProcessedMessage(eventId, "OrderCreatedIntegrationEvent");

        await using (var context = new NotificationsDbContext(_dbContextOptions))
        {
            var repository = new ProcessedMessageRepository(context);
            var existsBefore = await repository.ExistsAsync(eventId);
            Assert.False(existsBefore);

            await repository.AddAsync(processedMessage);
        }

        // Act & Assert 1 - Verificar existência no banco
        await using (var verifyContext = new NotificationsDbContext(_dbContextOptions))
        {
            var repository = new ProcessedMessageRepository(verifyContext);
            var existsAfter = await repository.ExistsAsync(eventId);
            Assert.True(existsAfter);
        }

        // Act & Assert 2 - Tentativa de inserção duplicada deve violar a restrição de unicidade/PK no PostgreSQL
        await using (var duplicateContext = new NotificationsDbContext(_dbContextOptions))
        {
            var repository = new ProcessedMessageRepository(duplicateContext);
            var duplicate = new ProcessedMessage(eventId, "OrderCreatedIntegrationEvent");
            await Assert.ThrowsAnyAsync<DbUpdateException>(() => repository.AddAsync(duplicate));
        }
    }
}
