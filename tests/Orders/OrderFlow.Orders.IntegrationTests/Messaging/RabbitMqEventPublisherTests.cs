using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using OrderFlow.Messaging.Contracts.Events;
using OrderFlow.Orders.Infrastructure.Messaging;
using RabbitMQ.Client;
using Xunit;

namespace OrderFlow.Orders.IntegrationTests.Messaging;

public class RabbitMqEventPublisherTests
{
    private readonly IRabbitMqConnection _connectionMock;
    private readonly IChannel _channelMock;
    private readonly IOptions<RabbitMqOptions> _options;
    private readonly ILogger<RabbitMqEventPublisher> _loggerMock;
    private readonly RabbitMqEventPublisher _publisher;

    public RabbitMqEventPublisherTests()
    {
        _connectionMock = Substitute.For<IRabbitMqConnection>();
        _channelMock = Substitute.For<IChannel>();
        _loggerMock = Substitute.For<ILogger<RabbitMqEventPublisher>>();

        var options = new RabbitMqOptions
        {
            HostName = "localhost",
            Port = 5672,
            UserName = "guest",
            Password = "guest",
            VirtualHost = "/",
            ExchangeName = "orderflow.orders",
            ExchangeType = "topic"
        };
        _options = Options.Create(options);

        _connectionMock.CreateChannelAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(_channelMock));

        _publisher = new RabbitMqEventPublisher(_connectionMock, _options, _loggerMock);
    }

    [Fact]
    public async Task PublishAsync_ShouldPublishMessage_WithCorrectRoutingKeyAndExchange()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var correlationId = "corr-12345";
        var integrationEvent = new OrderCreatedIntegrationEvent(
            orderId,
            "John Doe",
            "john@example.com",
            199.90m,
            "Pending",
            DateTimeOffset.UtcNow
        );
        var envelope = EventEnvelope<OrderCreatedIntegrationEvent>.Create(
            eventType: "OrderCreated",
            data: integrationEvent,
            correlationId: correlationId,
            eventId: eventId
        );

        string capturedExchange = null!;
        string capturedRoutingKey = null!;
        ReadOnlyMemory<byte> capturedBody = default;
        BasicProperties? capturedProperties = null;

        await _channelMock.BasicPublishAsync(
            Arg.Do<string>(x => capturedExchange = x),
            Arg.Do<string>(x => capturedRoutingKey = x),
            Arg.Any<bool>(),
            Arg.Do<BasicProperties>(x => capturedProperties = x),
            Arg.Do<ReadOnlyMemory<byte>>(x => capturedBody = x),
            Arg.Any<CancellationToken>()
        );

        // Act
        await _publisher.PublishAsync(envelope, "order.created");

        // Assert
        await _connectionMock.Received(1).InitializeTopologyAsync(Arg.Any<CancellationToken>());
        await _connectionMock.Received(1).CreateChannelAsync(Arg.Any<CancellationToken>());

        capturedExchange.Should().Be("orderflow.orders");
        capturedRoutingKey.Should().Be("order.created");
        capturedProperties.Should().NotBeNull();
        capturedProperties!.MessageId.Should().Be(eventId.ToString());
        capturedProperties.CorrelationId.Should().Be(correlationId);
        capturedProperties.Type.Should().Be("OrderCreated");
        capturedProperties.ContentType.Should().Be("application/json");
        capturedProperties.DeliveryMode.Should().Be(DeliveryModes.Persistent);

        var json = System.Text.Encoding.UTF8.GetString(capturedBody.Span);
        json.Should().Contain("OrderCreated");
        json.Should().Contain("John Doe");
        json.Should().Contain("john@example.com");
    }

    [Fact]
    public async Task PublishAsync_ShouldThrowAndLog_WhenChannelThrowsException()
    {
        // Arrange
        var envelope = EventEnvelope<OrderCompletedIntegrationEvent>.Create(
            "OrderCompleted",
            new OrderCompletedIntegrationEvent(Guid.NewGuid(), DateTimeOffset.UtcNow)
        );

        _channelMock.BasicPublishAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<bool>(),
            Arg.Any<BasicProperties>(),
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<CancellationToken>()
        ).Returns(ValueTask.FromException(new InvalidOperationException("Broker connection lost")));

        // Act
        var act = () => _publisher.PublishAsync(envelope, "order.completed");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Broker connection lost");
    }

    [Fact]
    public async Task PublishAsync_ShouldThrowArgumentNullException_WhenMessageIsNull()
    {
        // Act
        var act = () => _publisher.PublishAsync<object>(null!, "order.created");

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task PublishAsync_ShouldThrowArgumentException_WhenRoutingKeyIsInvalid(string? routingKey)
    {
        // Arrange
        var envelope = EventEnvelope<OrderCancelledIntegrationEvent>.Create(
            "OrderCancelled",
            new OrderCancelledIntegrationEvent(Guid.NewGuid(), "Pending", DateTimeOffset.UtcNow)
        );

        // Act
        var act = () => _publisher.PublishAsync(envelope, routingKey!);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }
}
