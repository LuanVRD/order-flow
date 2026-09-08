using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using OrderFlow.Messaging.Contracts.Events;
using OrderFlow.Notifications.Application;
using OrderFlow.Notifications.Infrastructure.Messaging;
using OrderFlow.Notifications.Infrastructure.Persistence;
using OrderFlow.Notifications.Infrastructure.Persistence.Repositories;
using RabbitMQ.Client;

namespace OrderFlow.Notifications.Tests.Infrastructure.Messaging;

public class OrderEventsConsumerEndToEndTests : IDisposable
{
    private readonly SqliteConnection _sqliteConnection;
    private readonly ServiceProvider _serviceProvider;
    private readonly OrderEventsConsumer _consumer;
    private readonly IChannel _channelMock;

    public OrderEventsConsumerEndToEndTests()
    {
        _sqliteConnection = new SqliteConnection("DataSource=:memory:");
        _sqliteConnection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNotificationsApplication();

        services.AddDbContext<NotificationsDbContext>(options =>
            options.UseSqlite(_sqliteConnection));

        services.AddScoped<OrderFlow.Notifications.Application.Interfaces.INotificationRepository, NotificationRepository>();
        services.AddScoped<OrderFlow.Notifications.Application.Interfaces.IProcessedMessageRepository, ProcessedMessageRepository>();

        var options = new RabbitMqOptions
        {
            HostName = "localhost",
            Port = 5672,
            ExchangeName = "orderflow.orders",
            QueueName = "orderflow.notifications"
        };
        services.AddSingleton(Options.Create(options));

        var connectionMock = Substitute.For<IRabbitMqConnection>();
        services.AddSingleton(connectionMock);

        _channelMock = Substitute.For<IChannel>();

        var loggerMock = Substitute.For<ILogger<OrderEventsConsumer>>();
        services.AddSingleton(loggerMock);

        services.AddSingleton<OrderEventsConsumer>();

        _serviceProvider = services.BuildServiceProvider();

        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        dbContext.Database.EnsureCreated();

        _consumer = _serviceProvider.GetRequiredService<OrderEventsConsumer>();
    }

    [Fact]
    public async Task ProcessMessageAsync_ShouldPersistNotificationAndProcessedMessageInDatabase_AndHandleIdempotency()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var correlationId = "corr-integration-test-01";

        var envelope = EventEnvelope<OrderCreatedIntegrationEvent>.Create(
            eventType: "OrderCreated",
            data: new OrderCreatedIntegrationEvent(
                orderId,
                "Alice Wonder",
                "alice@example.com",
                349.90m,
                "Pending",
                DateTimeOffset.UtcNow),
            correlationId: correlationId,
            eventId: eventId
        );

        var json = JsonSerializer.Serialize(envelope);
        var body = Encoding.UTF8.GetBytes(json);
        var propsMock = Substitute.For<IReadOnlyBasicProperties>();
        propsMock.Type.Returns("OrderCreated");

        // Act 1: Process the message for the first time
        await _consumer.ProcessMessageAsync(body, propsMock, 1UL, _channelMock);

        // Assert 1: Database has 1 notification and 1 processed message
        using (var scope1 = _serviceProvider.CreateScope())
        {
            var db = scope1.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            var notifications = await db.Notifications.ToListAsync();
            var processed = await db.ProcessedMessages.ToListAsync();

            notifications.Should().HaveCount(1);
            notifications[0].OrderId.Should().Be(orderId);
            notifications[0].Message.Should().Contain("Alice Wonder");

            processed.Should().HaveCount(1);
            processed[0].EventId.Should().Be(eventId);
            processed[0].EventType.Should().Be("OrderCreated");
        }

        await _channelMock.Received(1).BasicAckAsync(1UL, false, Arg.Any<CancellationToken>());

        // Act 2: Process the identical message again (replay / duplicate delivery)
        await _consumer.ProcessMessageAsync(body, propsMock, 2UL, _channelMock);

        // Assert 2: Database still has exactly 1 notification and 1 processed message (idempotency preserved)
        using (var scope2 = _serviceProvider.CreateScope())
        {
            var db = scope2.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            var notifications = await db.Notifications.ToListAsync();
            var processed = await db.ProcessedMessages.ToListAsync();

            notifications.Should().HaveCount(1);
            processed.Should().HaveCount(1);
        }

        await _channelMock.Received(1).BasicAckAsync(2UL, false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessMessageAsync_ShouldForwardToDlq_WhenPoisonMessageIsReceived()
    {
        // Arrange
        var invalidPayload = Encoding.UTF8.GetBytes("{ \"invalidJson\": [corrupted }");
        var propsMock = Substitute.For<IReadOnlyBasicProperties>();
        propsMock.Type.Returns("OrderCreated");
        propsMock.MessageId.Returns(Guid.NewGuid().ToString());
        propsMock.CorrelationId.Returns("corr-poison-test");

        // Act
        await _consumer.ProcessMessageAsync(invalidPayload, propsMock, 3UL, _channelMock);

        // Assert: Poison message is rejected with requeue: false, heading to DLQ
        await _channelMock.Received(1).BasicNackAsync(3UL, false, false, Arg.Any<CancellationToken>());
        await _channelMock.DidNotReceive().BasicAckAsync(3UL, Arg.Any<bool>(), Arg.Any<CancellationToken>());

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var notifications = await db.Notifications.ToListAsync();
        notifications.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessMessageAsync_ShouldForwardToDlq_WhenDatabaseErrorPersistsAcrossRetries()
    {
        // Arrange
        // Simulate database disruption by closing the connection
        await _sqliteConnection.CloseAsync();

        var orderId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var envelope = EventEnvelope<OrderCreatedIntegrationEvent>.Create(
            eventType: "OrderCreated",
            data: new OrderCreatedIntegrationEvent(
                orderId,
                "Error Scenario",
                "err@example.com",
                99m,
                "Pending",
                DateTimeOffset.UtcNow),
            correlationId: "corr-db-error-01",
            eventId: eventId
        );

        var json = JsonSerializer.Serialize(envelope);
        var body = Encoding.UTF8.GetBytes(json);
        var propsMock = Substitute.For<IReadOnlyBasicProperties>();
        propsMock.Type.Returns("OrderCreated");

        // Act
        await _consumer.ProcessMessageAsync(body, propsMock, 4UL, _channelMock);

        // Assert: Retries exhausted, message sent to DLQ via BasicNack(requeue: false)
        await _channelMock.Received(1).BasicNackAsync(4UL, false, false, Arg.Any<CancellationToken>());
        await _channelMock.DidNotReceive().BasicAckAsync(4UL, Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        _sqliteConnection.Dispose();
    }
}
