using Microsoft.EntityFrameworkCore;
using OrderFlow.Messaging.Contracts.Events;
using OrderFlow.Notifications.Application.UseCases;
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
            await context.SaveChangesAsync();
        }

        // Act & Assert 1 - Verificar existência no banco
        await using (var verifyContext = new NotificationsDbContext(_dbContextOptions))
        {
            var repository = new ProcessedMessageRepository(verifyContext);
            var existsAfter = await repository.ExistsAsync(eventId);
            Assert.True(existsAfter);
        }

        // Act & Assert 2 - Tentativa de inserção duplicada deve violar a restrição de unicidade/PK no PostgreSQL no commit
        await using (var duplicateContext = new NotificationsDbContext(_dbContextOptions))
        {
            var repository = new ProcessedMessageRepository(duplicateContext);
            var duplicate = new ProcessedMessage(eventId, "OrderCreatedIntegrationEvent");
            await repository.AddAsync(duplicate);
            await Assert.ThrowsAnyAsync<DbUpdateException>(() => duplicateContext.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenFailureOccursBeforeCommit_ShouldNotPersistAnyNotificationOrProcessedMessage()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var payload = new OrderCreatedIntegrationEvent(
            orderId,
            "Failed Commit User",
            "fail@example.com",
            120m,
            "Pending",
            DateTimeOffset.UtcNow);

        var envelope = EventEnvelope<OrderCreatedIntegrationEvent>.Create(
            eventType: "OrderCreated",
            data: payload,
            eventId: eventId);

        // Act: Simular falha adicionando ao DbContext mas abortando/falhando antes de CommitAsync
        await using (var context = new NotificationsDbContext(_dbContextOptions))
        {
            var notifRepo = new NotificationRepository(context);
            var procRepo = new ProcessedMessageRepository(context);

            var notification = new Notification(orderId, NotificationType.OrderCreated, "Mensagem de teste");
            var processedMessage = new ProcessedMessage(eventId, "OrderCreated");

            await notifRepo.AddAsync(notification);
            await procRepo.AddAsync(processedMessage);

            // Simula crash / abort / cancelamento sem chamar SaveChangesAsync
        }

        // Assert: Nenhuma entidade deve ter sido persistida no PostgreSQL
        await using (var verifyContext = new NotificationsDbContext(_dbContextOptions))
        {
            var persistedNotification = await verifyContext.Notifications.FirstOrDefaultAsync(n => n.OrderId == orderId);
            var persistedMessage = await verifyContext.ProcessedMessages.FirstOrDefaultAsync(p => p.EventId == eventId);

            Assert.Null(persistedNotification);
            Assert.Null(persistedMessage);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenConcurrentExecutionsWithSameEventId_ShouldPersistExactlyOneNotificationAndOneProcessedMessage()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var payload = new OrderCreatedIntegrationEvent(
            orderId,
            "Concurrent User",
            "concurrent@example.com",
            300m,
            "Pending",
            DateTimeOffset.UtcNow);

        var envelope = EventEnvelope<OrderCreatedIntegrationEvent>.Create(
            eventType: "OrderCreated",
            data: payload,
            eventId: eventId);

        // Duas instâncias de DbContext e UseCase simulando duas instâncias de worker / threads consumindo o mesmo evento concorrentemente
        await using var context1 = new NotificationsDbContext(_dbContextOptions);
        await using var context2 = new NotificationsDbContext(_dbContextOptions);

        var useCase1 = new ProcessOrderCreatedEventUseCase(
            new NotificationRepository(context1),
            new ProcessedMessageRepository(context1),
            new UnitOfWork(context1));

        var useCase2 = new ProcessOrderCreatedEventUseCase(
            new NotificationRepository(context2),
            new ProcessedMessageRepository(context2),
            new UnitOfWork(context2));

        // Act: Executar simultaneamente
        var task1 = Task.Run(() => useCase1.ExecuteAsync(envelope));
        var task2 = Task.Run(() => useCase2.ExecuteAsync(envelope));

        var results = await Task.WhenAll(task1, task2);

        // Assert:
        // Exatamente um resultado deve ser a notificação criada, e o outro deve ser null (absorvido como duplicado)
        var nonNullResults = results.Where(r => r != null).ToList();
        var nullResults = results.Where(r => r == null).ToList();

        Assert.Single(nonNullResults);
        Assert.Single(nullResults);
        Assert.Equal(orderId, nonNullResults[0]!.OrderId);

        // Verificar banco PostgreSQL: exatamente 1 notificação e 1 ProcessedMessage
        await using var verifyContext = new NotificationsDbContext(_dbContextOptions);
        var persistedNotifications = await verifyContext.Notifications.Where(n => n.OrderId == orderId).ToListAsync();
        var persistedMessages = await verifyContext.ProcessedMessages.Where(p => p.EventId == eventId).ToListAsync();

        Assert.Single(persistedNotifications);
        Assert.Single(persistedMessages);
    }

    [Fact]
    public async Task ExecuteAsync_WhenSequentialDuplicateEventReceived_ShouldReturnNullAndNotDuplicateNotification()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var payload = new OrderCreatedIntegrationEvent(
            orderId,
            "Sequential Duplicate User",
            "seq@example.com",
            450m,
            "Pending",
            DateTimeOffset.UtcNow);

        var envelope = EventEnvelope<OrderCreatedIntegrationEvent>.Create(
            eventType: "OrderCreated",
            data: payload,
            eventId: eventId);

        // 1ª Execução: Primeira entrega
        await using (var context1 = new NotificationsDbContext(_dbContextOptions))
        {
            var useCase = new ProcessOrderCreatedEventUseCase(
                new NotificationRepository(context1),
                new ProcessedMessageRepository(context1),
                new UnitOfWork(context1));

            var result1 = await useCase.ExecuteAsync(envelope);
            Assert.NotNull(result1);
            Assert.Equal(orderId, result1.OrderId);
        }

        // 2ª Execução: Reentrega da mesma mensagem
        await using (var context2 = new NotificationsDbContext(_dbContextOptions))
        {
            var useCase = new ProcessOrderCreatedEventUseCase(
                new NotificationRepository(context2),
                new ProcessedMessageRepository(context2),
                new UnitOfWork(context2));

            var result2 = await useCase.ExecuteAsync(envelope);
            Assert.Null(result2);
        }

        // Assert: Apenas 1 registro no PostgreSQL
        await using var verifyContext = new NotificationsDbContext(_dbContextOptions);
        var totalNotifications = await verifyContext.Notifications.CountAsync(n => n.OrderId == orderId);
        var totalProcessed = await verifyContext.ProcessedMessages.CountAsync(p => p.EventId == eventId);

        Assert.Equal(1, totalNotifications);
        Assert.Equal(1, totalProcessed);
    }
}
