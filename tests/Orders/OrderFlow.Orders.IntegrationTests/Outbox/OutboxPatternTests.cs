using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using OrderFlow.Messaging.Contracts.Correlation;
using OrderFlow.Messaging.Contracts.Events;
using OrderFlow.Orders.Application.DTOs;
using OrderFlow.Orders.Application.Interfaces;
using OrderFlow.Orders.Application.UseCases;
using OrderFlow.Orders.Application.Validators;
using OrderFlow.Orders.Domain.Entities;
using OrderFlow.Orders.Domain.Enums;
using OrderFlow.Orders.Infrastructure.Messaging;
using OrderFlow.Orders.Infrastructure.Outbox;
using OrderFlow.Orders.Infrastructure.Persistence;
using OrderFlow.Orders.Infrastructure.Persistence.Interceptors;
using OrderFlow.Orders.Infrastructure.Persistence.Repositories;

namespace OrderFlow.Orders.IntegrationTests.Outbox;

public class OutboxPatternTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OrdersDbContext _dbContext;
    private readonly IOrderRepository _orderRepository;
    private readonly IOutboxRepository _outboxRepository;
    private readonly IEventPublisher _eventPublisherMock;
    private readonly ILogger<OutboxProcessorBackgroundService> _loggerMock;
    private readonly ICorrelationContextAccessor _correlationContextAccessorMock;
    private readonly CreateOrderUseCase _createOrderUseCase;
    private readonly ChangeOrderStatusUseCase _changeOrderStatusUseCase;
    private readonly CancelOrderUseCase _cancelOrderUseCase;
    private readonly IServiceProvider _serviceProvider;

    public OutboxPatternTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _correlationContextAccessorMock = Substitute.For<ICorrelationContextAccessor>();
        _correlationContextAccessorMock.CorrelationId.Returns("test-corr-outbox-12345");

        var interceptor = new OutboxSaveChangesInterceptor(_correlationContextAccessorMock);

        var options = new DbContextOptionsBuilder<OrdersDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(interceptor)
            .Options;

        _dbContext = new OrdersDbContext(options, interceptor);
        _dbContext.Database.EnsureCreated();

        _orderRepository = new OrderRepository(_dbContext);
        _outboxRepository = new OutboxRepository(_dbContext);

        _eventPublisherMock = Substitute.For<IEventPublisher>();
        _loggerMock = Substitute.For<ILogger<OutboxProcessorBackgroundService>>();

        var createValidator = new CreateOrderCommandValidator();
        var changeValidator = new ChangeOrderStatusCommandValidator();
        var cancelValidator = new CancelOrderCommandValidator();

        _createOrderUseCase = new CreateOrderUseCase(_orderRepository, createValidator);
        _changeOrderStatusUseCase = new ChangeOrderStatusUseCase(_orderRepository, changeValidator);
        _cancelOrderUseCase = new CancelOrderUseCase(_orderRepository, cancelValidator);

        var services = new ServiceCollection();
        services.AddSingleton(_dbContext);
        services.AddScoped<IOutboxRepository>(_ => new OutboxRepository(_dbContext));
        services.AddScoped<IEventPublisher>(_ => _eventPublisherMock);
        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task PersistOrder_ShouldAtomicallyPersistOrderAndOutboxMessageInSameTransaction()
    {
        // Arrange
        var command = new CreateOrderCommand("Alice Outbox", "alice@example.com", 250.00m);

        // Act
        var result = await _createOrderUseCase.ExecuteAsync(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();

        // 1. Verify Order in DB
        var savedOrder = await _dbContext.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == result.Id);
        savedOrder.Should().NotBeNull();
        savedOrder!.CustomerName.Should().Be("Alice Outbox");
        savedOrder.DomainEvents.Should().BeEmpty(); // Domain events cleared after outbox creation

        // 2. Verify OutboxMessage in DB
        var outboxMessage = await _dbContext.OutboxMessages.AsNoTracking().FirstOrDefaultAsync();
        outboxMessage.Should().NotBeNull();
        outboxMessage!.Type.Should().Be("OrderCreated");
        outboxMessage.Version.Should().Be(1);
        outboxMessage.ProcessedAt.Should().BeNull();
        outboxMessage.RetryCount.Should().Be(0);
        outboxMessage.LastError.Should().BeNull();

        var envelope = JsonSerializer.Deserialize<EventEnvelope<OrderCreatedIntegrationEvent>>(
            outboxMessage.Payload,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        envelope.Should().NotBeNull();
        envelope!.Data.OrderId.Should().Be(result.Id);
        envelope.Data.CustomerName.Should().Be("Alice Outbox");
        envelope.CorrelationId.Should().Be("test-corr-outbox-12345");
    }

    [Fact]
    public async Task BrokerUnavailability_ShouldNotFailOrderCreation_AndOutboxShouldRetainEventForRetry()
    {
        // Arrange - RabbitMQ broker is failing/unreachable
        _eventPublisherMock.PublishAsync(
            Arg.Any<object>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>()
        ).ThrowsAsync(new InvalidOperationException("RabbitMQ connection refused: Broker unreachable"));

        var command = new CreateOrderCommand("Resilience Test", "resilience@example.com", 199.90m);

        // Act 1: Order creation succeeds without calling RabbitMQ directly
        var result = await _createOrderUseCase.ExecuteAsync(command, CancellationToken.None);
        result.Should().NotBeNull();

        var savedOrder = await _dbContext.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == result.Id);
        savedOrder.Should().NotBeNull();

        // Act 2: Outbox background processor attempts to publish pending batch
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(_ => _serviceProvider.CreateScope());

        var outboxOptions = Options.Create(new OutboxOptions
        {
            BatchSize = 10,
            LockDurationSeconds = 30,
            BaseDelaySeconds = 1,
            MaxDelaySeconds = 10
        });

        var processor = new OutboxProcessorBackgroundService(scopeFactory, outboxOptions, _loggerMock);
        var successCount = await processor.ProcessPendingBatchAsync(CancellationToken.None);

        // Assert 2: Publish failed, 0 successes, message retained with failure details
        successCount.Should().Be(0);

        var pendingMessage = await _dbContext.OutboxMessages.AsNoTracking().FirstOrDefaultAsync(m => m.Id == result.Id || m.Type == "OrderCreated");
        pendingMessage.Should().NotBeNull();
        pendingMessage!.ProcessedAt.Should().BeNull();
        pendingMessage.RetryCount.Should().Be(1);
        pendingMessage.LastError.Should().Contain("RabbitMQ connection refused");
        pendingMessage.NextRetryAtUtc.Should().NotBeNull();
        pendingMessage.LockId.Should().BeNull(); // Lock released on failure

        // Act 3: Broker recovers!
        _eventPublisherMock.PublishAsync(
            Arg.Any<object>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>()
        ).Returns(Task.CompletedTask);

        // Reset NextRetryAtUtc to simulate backoff elapsing
        var elapsedRetryTime = DateTimeOffset.UtcNow.AddSeconds(-1);
        await _dbContext.OutboxMessages
            .Where(m => m.Id == pendingMessage.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.NextRetryAtUtc, elapsedRetryTime));

        var recoverySuccessCount = await processor.ProcessPendingBatchAsync(CancellationToken.None);

        // Assert 3: Message is published and marked as processed
        recoverySuccessCount.Should().Be(1);

        var processedMessage = await _dbContext.OutboxMessages.AsNoTracking().FirstOrDefaultAsync(m => m.Id == pendingMessage.Id);
        processedMessage.Should().NotBeNull();
        processedMessage!.ProcessedAt.Should().NotBeNull();
        processedMessage.LastError.Should().BeNull();

        await _eventPublisherMock.Received(2).PublishAsync(
            Arg.Any<object>(),
            Arg.Is<string>(rk => rk == "order.created"),
            Arg.Any<CancellationToken>()
        );
    }

    [Fact]
    public async Task MultipleInstances_ShouldNotProcessSameMessageConcurrently()
    {
        // Arrange
        var command = new CreateOrderCommand("Locking Test", "lock@example.com", 300.00m);
        await _createOrderUseCase.ExecuteAsync(command, CancellationToken.None);

        var outboxRepo = new OutboxRepository(_dbContext);

        // Act: Worker 1 locks the message
        var worker1Messages = await outboxRepo.FetchAndLockPendingMessagesAsync(
            batchSize: 10,
            lockId: "worker-instance-1",
            lockDuration: TimeSpan.FromSeconds(30),
            CancellationToken.None);

        // Worker 2 attempts to fetch pending messages concurrently
        var worker2Messages = await outboxRepo.FetchAndLockPendingMessagesAsync(
            batchSize: 10,
            lockId: "worker-instance-2",
            lockDuration: TimeSpan.FromSeconds(30),
            CancellationToken.None);

        // Assert
        worker1Messages.Should().ContainSingle();
        worker2Messages.Should().BeEmpty(); // Worker 2 cannot lock messages held by Worker 1
    }

    [Fact]
    public async Task ChangeStatusAndCancel_ShouldProduceMultipleOutboxMessagesAtomically()
    {
        // Arrange
        var createCommand = new CreateOrderCommand("State Flow User", "state@example.com", 500.00m);
        var created = await _createOrderUseCase.ExecuteAsync(createCommand, CancellationToken.None);

        // Act 1: Change to Processing
        await _changeOrderStatusUseCase.ExecuteAsync(
            new ChangeOrderStatusCommand(created.Id, OrderStatus.Processing),
            CancellationToken.None);

        // Act 2: Cancel order
        await _cancelOrderUseCase.ExecuteAsync(
            new CancelOrderCommand(created.Id),
            CancellationToken.None);

        // Assert
        var messages = await _dbContext.OutboxMessages.AsNoTracking().ToListAsync();

        // 1: OrderCreated
        // 2: OrderStatusChanged (Pending -> Processing)
        // 3: OrderStatusChanged (Processing -> Cancelled)
        // 4: OrderCancelled
        messages.Should().HaveCount(4);
        messages.Select(m => m.Type).Should().BeEquivalentTo(new[]
        {
            "OrderCreated",
            "OrderStatusChanged",
            "OrderStatusChanged",
            "OrderCancelled"
        });
    }

    [Fact]
    public async Task OutboxCleanup_ShouldPurgeProcessedMessagesOlderThanRetentionPeriod_AndKeepRecentAndPending()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;

        // Old processed message (10 days old, retention is 7 days) -> should be deleted
        var oldProcessed = new OutboxMessage(Guid.NewGuid(), "OrderCreated", 1, "{}", now.AddDays(-10));
        oldProcessed.MarkAsProcessed(now.AddDays(-10));

        // Recent processed message (2 days old) -> should be kept
        var recentProcessed = new OutboxMessage(Guid.NewGuid(), "OrderCreated", 1, "{}", now.AddDays(-2));
        recentProcessed.MarkAsProcessed(now.AddDays(-2));

        // Unprocessed pending message (10 days old) -> must NEVER be deleted
        var oldPending = new OutboxMessage(Guid.NewGuid(), "OrderCreated", 1, "{}", now.AddDays(-10));

        await _dbContext.OutboxMessages.AddRangeAsync(oldProcessed, recentProcessed, oldPending);
        await _dbContext.SaveChangesAsync();

        // Act
        var cutoff = now.AddDays(-7);
        var deletedCount = await _outboxRepository.PurgeProcessedMessagesAsync(cutoff, CancellationToken.None);

        // Assert
        deletedCount.Should().Be(1);

        var remainingMessages = await _dbContext.OutboxMessages.AsNoTracking().ToListAsync();
        remainingMessages.Should().HaveCount(2);
        remainingMessages.Should().NotContain(m => m.Id == oldProcessed.Id);
        remainingMessages.Should().Contain(m => m.Id == recentProcessed.Id);
        remainingMessages.Should().Contain(m => m.Id == oldPending.Id);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }
}
