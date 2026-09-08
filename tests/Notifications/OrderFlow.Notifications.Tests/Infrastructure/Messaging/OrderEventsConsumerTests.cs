using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using OrderFlow.Messaging.Contracts.Events;
using OrderFlow.Notifications.Application.Interfaces;
using OrderFlow.Notifications.Application.UseCases;
using OrderFlow.Notifications.Domain.Entities;
using OrderFlow.Notifications.Domain.Enums;
using OrderFlow.Notifications.Infrastructure.Messaging;
using RabbitMQ.Client;

namespace OrderFlow.Notifications.Tests.Infrastructure.Messaging;

public class OrderEventsConsumerTests
{
    private readonly IRabbitMqConnection _connectionMock;
    private readonly IServiceScopeFactory _scopeFactoryMock;
    private readonly IServiceScope _scopeMock;
    private readonly IServiceProvider _serviceProviderMock;
    private readonly IOptions<RabbitMqOptions> _options;
    private readonly ILogger<OrderEventsConsumer> _loggerMock;
    private readonly IChannel _channelMock;
    private readonly OrderEventsConsumer _consumer;

    private readonly INotificationRepository _notificationRepositoryMock;
    private readonly IProcessedMessageRepository _processedMessageRepositoryMock;

    public OrderEventsConsumerTests()
    {
        _connectionMock = Substitute.For<IRabbitMqConnection>();
        _scopeFactoryMock = Substitute.For<IServiceScopeFactory>();
        _scopeMock = Substitute.For<IServiceScope>();
        _serviceProviderMock = Substitute.For<IServiceProvider>();
        _loggerMock = Substitute.For<ILogger<OrderEventsConsumer>>();
        _channelMock = Substitute.For<IChannel>();

        _notificationRepositoryMock = Substitute.For<INotificationRepository>();
        _processedMessageRepositoryMock = Substitute.For<IProcessedMessageRepository>();

        _scopeFactoryMock.CreateScope().Returns(_scopeMock);
        _scopeMock.ServiceProvider.Returns(_serviceProviderMock);

        _serviceProviderMock.GetService(typeof(ProcessOrderCreatedEventUseCase))
            .Returns(new ProcessOrderCreatedEventUseCase(_notificationRepositoryMock, _processedMessageRepositoryMock));
        _serviceProviderMock.GetService(typeof(ProcessOrderStatusChangedEventUseCase))
            .Returns(new ProcessOrderStatusChangedEventUseCase(_notificationRepositoryMock, _processedMessageRepositoryMock));
        _serviceProviderMock.GetService(typeof(ProcessOrderCompletedEventUseCase))
            .Returns(new ProcessOrderCompletedEventUseCase(_notificationRepositoryMock, _processedMessageRepositoryMock));
        _serviceProviderMock.GetService(typeof(ProcessOrderCancelledEventUseCase))
            .Returns(new ProcessOrderCancelledEventUseCase(_notificationRepositoryMock, _processedMessageRepositoryMock));

        var options = new RabbitMqOptions
        {
            HostName = "localhost",
            Port = 5672,
            UserName = "guest",
            Password = "guest",
            VirtualHost = "/",
            ExchangeName = "orderflow.orders",
            ExchangeType = "topic",
            QueueName = "orderflow.notifications",
            DeadLetterExchangeName = "orderflow.notifications.dlx",
            DeadLetterQueueName = "orderflow.notifications.dlq",
            DeadLetterRoutingKey = "orderflow.notifications.dlq",
            PrefetchCount = 10,
            MaxRetryAttempts = 3,
            InitialRetryDelayMs = 1
        };
        _options = Options.Create(options);

        _consumer = new OrderEventsConsumer(
            _connectionMock,
            _scopeFactoryMock,
            _options,
            _loggerMock);
    }

    [Fact]
    public async Task ProcessMessageAsync_ShouldProcessOrderCreatedAndAck_WhenValidMessageReceived()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var envelope = EventEnvelope<OrderCreatedIntegrationEvent>.Create(
            eventType: "OrderCreated",
            data: new OrderCreatedIntegrationEvent(
                orderId,
                "Jane Doe",
                "jane@example.com",
                250.00m,
                "Pending",
                DateTimeOffset.UtcNow),
            correlationId: "corr-001",
            eventId: eventId
        );

        var json = JsonSerializer.Serialize(envelope);
        var body = Encoding.UTF8.GetBytes(json);
        var propsMock = Substitute.For<IReadOnlyBasicProperties>();
        propsMock.Type.Returns("OrderCreated");

        _processedMessageRepositoryMock.ExistsAsync(eventId, Arg.Any<CancellationToken>())
            .Returns(false);

        // Act
        await _consumer.ProcessMessageAsync(body, propsMock, 1UL, _channelMock);

        // Assert
        await _notificationRepositoryMock.Received(1).AddAsync(
            Arg.Is<Notification>(n => n.OrderId == orderId && n.Type == NotificationType.OrderCreated),
            Arg.Any<CancellationToken>());

        await _processedMessageRepositoryMock.Received(1).AddAsync(
            Arg.Is<ProcessedMessage>(p => p.EventId == eventId && p.EventType == "OrderCreated"),
            Arg.Any<CancellationToken>());

        await _channelMock.Received(1).BasicAckAsync(1UL, false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessMessageAsync_ShouldProcessOrderStatusChangedAndAck_WhenValidMessageReceived()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var envelope = EventEnvelope<OrderStatusChangedIntegrationEvent>.Create(
            eventType: "OrderStatusChanged",
            data: new OrderStatusChangedIntegrationEvent(
                orderId,
                "Pending",
                "Processing",
                DateTimeOffset.UtcNow),
            correlationId: "corr-002",
            eventId: eventId
        );

        var json = JsonSerializer.Serialize(envelope);
        var body = Encoding.UTF8.GetBytes(json);
        var propsMock = Substitute.For<IReadOnlyBasicProperties>();
        propsMock.Type.Returns("OrderStatusChanged");

        _processedMessageRepositoryMock.ExistsAsync(eventId, Arg.Any<CancellationToken>())
            .Returns(false);

        // Act
        await _consumer.ProcessMessageAsync(body, propsMock, 2UL, _channelMock);

        // Assert
        await _notificationRepositoryMock.Received(1).AddAsync(
            Arg.Is<Notification>(n => n.OrderId == orderId && n.Type == NotificationType.OrderStatusChanged),
            Arg.Any<CancellationToken>());

        await _channelMock.Received(1).BasicAckAsync(2UL, false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessMessageAsync_ShouldProcessOrderCompletedAndAck_WhenValidMessageReceived()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var envelope = EventEnvelope<OrderCompletedIntegrationEvent>.Create(
            eventType: "OrderCompleted",
            data: new OrderCompletedIntegrationEvent(
                orderId,
                DateTimeOffset.UtcNow),
            correlationId: "corr-003",
            eventId: eventId
        );

        var json = JsonSerializer.Serialize(envelope);
        var body = Encoding.UTF8.GetBytes(json);
        var propsMock = Substitute.For<IReadOnlyBasicProperties>();
        propsMock.Type.Returns("OrderCompleted");

        _processedMessageRepositoryMock.ExistsAsync(eventId, Arg.Any<CancellationToken>())
            .Returns(false);

        // Act
        await _consumer.ProcessMessageAsync(body, propsMock, 3UL, _channelMock);

        // Assert
        await _notificationRepositoryMock.Received(1).AddAsync(
            Arg.Is<Notification>(n => n.OrderId == orderId && n.Type == NotificationType.OrderCompleted),
            Arg.Any<CancellationToken>());

        await _channelMock.Received(1).BasicAckAsync(3UL, false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessMessageAsync_ShouldProcessOrderCancelledAndAck_WhenValidMessageReceived()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var envelope = EventEnvelope<OrderCancelledIntegrationEvent>.Create(
            eventType: "OrderCancelled",
            data: new OrderCancelledIntegrationEvent(
                orderId,
                "Processing",
                DateTimeOffset.UtcNow,
                "Customer requested cancellation"),
            correlationId: "corr-004",
            eventId: eventId
        );

        var json = JsonSerializer.Serialize(envelope);
        var body = Encoding.UTF8.GetBytes(json);
        var propsMock = Substitute.For<IReadOnlyBasicProperties>();
        propsMock.Type.Returns("OrderCancelled");

        _processedMessageRepositoryMock.ExistsAsync(eventId, Arg.Any<CancellationToken>())
            .Returns(false);

        // Act
        await _consumer.ProcessMessageAsync(body, propsMock, 4UL, _channelMock);

        // Assert
        await _notificationRepositoryMock.Received(1).AddAsync(
            Arg.Is<Notification>(n => n.OrderId == orderId && n.Type == NotificationType.OrderCancelled),
            Arg.Any<CancellationToken>());

        await _channelMock.Received(1).BasicAckAsync(4UL, false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessMessageAsync_ShouldAckWithoutCreatingNotification_WhenMessageIsDuplicate()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var envelope = EventEnvelope<OrderCreatedIntegrationEvent>.Create(
            eventType: "OrderCreated",
            data: new OrderCreatedIntegrationEvent(
                Guid.NewGuid(),
                "Duplicate User",
                "dup@example.com",
                100m,
                "Pending",
                DateTimeOffset.UtcNow),
            eventId: eventId
        );

        var json = JsonSerializer.Serialize(envelope);
        var body = Encoding.UTF8.GetBytes(json);
        var propsMock = Substitute.For<IReadOnlyBasicProperties>();
        propsMock.Type.Returns("OrderCreated");

        _processedMessageRepositoryMock.ExistsAsync(eventId, Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        await _consumer.ProcessMessageAsync(body, propsMock, 5UL, _channelMock);

        // Assert
        await _notificationRepositoryMock.DidNotReceive().AddAsync(
            Arg.Any<Notification>(),
            Arg.Any<CancellationToken>());

        await _channelMock.Received(1).BasicAckAsync(5UL, false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessMessageAsync_ShouldNackWithoutRequeueToDlq_WhenJsonIsInvalid()
    {
        // Arrange
        var invalidJsonBytes = Encoding.UTF8.GetBytes("{ invalid_json ");
        var propsMock = Substitute.For<IReadOnlyBasicProperties>();
        propsMock.Type.Returns("OrderCreated");

        // Act
        await _consumer.ProcessMessageAsync(invalidJsonBytes, propsMock, 6UL, _channelMock);

        // Assert: Nack with requeue: false forwards immediately to DLQ
        await _channelMock.Received(1).BasicNackAsync(6UL, false, false, Arg.Any<CancellationToken>());
        await _channelMock.DidNotReceive().BasicAckAsync(Arg.Any<ulong>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessMessageAsync_ShouldNackWithoutRequeueToDlq_WhenEventTypeIsUnsupported()
    {
        // Arrange
        var json = "{\"eventType\":\"UnknownEvent\",\"data\":{}}";
        var body = Encoding.UTF8.GetBytes(json);
        var propsMock = Substitute.For<IReadOnlyBasicProperties>();
        propsMock.Type.Returns("UnknownEvent");

        // Act
        await _consumer.ProcessMessageAsync(body, propsMock, 7UL, _channelMock);

        // Assert: Nack with requeue: false forwards immediately to DLQ
        await _channelMock.Received(1).BasicNackAsync(7UL, false, false, Arg.Any<CancellationToken>());
        await _channelMock.DidNotReceive().BasicAckAsync(Arg.Any<ulong>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessMessageAsync_ShouldNackWithoutRequeueToDlq_WhenEnvelopeDataIsNull()
    {
        // Arrange
        var json = "{\"eventId\":\"" + Guid.NewGuid() + "\",\"eventType\":\"OrderCreated\",\"data\":null}";
        var body = Encoding.UTF8.GetBytes(json);
        var propsMock = Substitute.For<IReadOnlyBasicProperties>();
        propsMock.Type.Returns("OrderCreated");

        // Act
        await _consumer.ProcessMessageAsync(body, propsMock, 8UL, _channelMock);

        // Assert: Nack with requeue: false forwards immediately to DLQ
        await _channelMock.Received(1).BasicNackAsync(8UL, false, false, Arg.Any<CancellationToken>());
        await _channelMock.DidNotReceive().BasicAckAsync(Arg.Any<ulong>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessMessageAsync_ShouldRetryAndAck_WhenTransientExceptionRecoversWithinMaxAttempts()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var envelope = EventEnvelope<OrderCreatedIntegrationEvent>.Create(
            eventType: "OrderCreated",
            data: new OrderCreatedIntegrationEvent(
                orderId,
                "Retry Success User",
                "retry@example.com",
                80m,
                "Pending",
                DateTimeOffset.UtcNow),
            correlationId: "corr-retry-01",
            eventId: eventId
        );

        var json = JsonSerializer.Serialize(envelope);
        var body = Encoding.UTF8.GetBytes(json);
        var propsMock = Substitute.For<IReadOnlyBasicProperties>();
        propsMock.Type.Returns("OrderCreated");

        int callCount = 0;
        _processedMessageRepositoryMock.ExistsAsync(eventId, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callCount++;
                if (callCount == 1)
                {
                    throw new InvalidOperationException("Transient database timeout on attempt 1");
                }
                return Task.FromResult(false);
            });

        // Act
        await _consumer.ProcessMessageAsync(body, propsMock, 9UL, _channelMock);

        // Assert
        callCount.Should().Be(2);
        await _notificationRepositoryMock.Received(1).AddAsync(
            Arg.Is<Notification>(n => n.OrderId == orderId),
            Arg.Any<CancellationToken>());
        await _channelMock.Received(1).BasicAckAsync(9UL, false, Arg.Any<CancellationToken>());
        await _channelMock.DidNotReceive().BasicNackAsync(Arg.Any<ulong>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessMessageAsync_ShouldNackWithoutRequeueToDlq_WhenTransientExceptionExhaustsAllRetries()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var envelope = EventEnvelope<OrderCreatedIntegrationEvent>.Create(
            eventType: "OrderCreated",
            data: new OrderCreatedIntegrationEvent(
                Guid.NewGuid(),
                "Error User",
                "err@example.com",
                50m,
                "Pending",
                DateTimeOffset.UtcNow),
            eventId: eventId
        );

        var json = JsonSerializer.Serialize(envelope);
        var body = Encoding.UTF8.GetBytes(json);
        var propsMock = Substitute.For<IReadOnlyBasicProperties>();
        propsMock.Type.Returns("OrderCreated");

        _processedMessageRepositoryMock.ExistsAsync(eventId, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Persistent database outage"));

        // Act
        await _consumer.ProcessMessageAsync(body, propsMock, 10UL, _channelMock);

        // Assert: Exhausted all 3 attempts, message sent to DLQ via BasicNack(requeue: false)
        await _processedMessageRepositoryMock.Received(3).ExistsAsync(eventId, Arg.Any<CancellationToken>());
        await _channelMock.Received(1).BasicNackAsync(10UL, false, false, Arg.Any<CancellationToken>());
        await _channelMock.DidNotReceive().BasicAckAsync(Arg.Any<ulong>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }
}
