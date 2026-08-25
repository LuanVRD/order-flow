using FluentAssertions;
using OrderFlow.Notifications.Domain.Entities;
using OrderFlow.Notifications.Domain.Enums;
using OrderFlow.Notifications.Domain.Exceptions;

namespace OrderFlow.Notifications.Tests.Domain;

public class NotificationTests
{
    [Fact]
    public void Constructor_WithValidParameters_ShouldCreateNotification()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var type = NotificationType.OrderCreated;
        var message = "Pedido criado com sucesso.";

        // Act
        var notification = new Notification(orderId, type, message);

        // Assert
        notification.Id.Should().NotBeEmpty();
        notification.OrderId.Should().Be(orderId);
        notification.Type.Should().Be(type);
        notification.Message.Should().Be(message);
        notification.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Constructor_WithExplicitIdAndCreatedAt_ShouldRetainValues()
    {
        // Arrange
        var id = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var type = NotificationType.OrderStatusChanged;
        var message = "Status atualizado.";

        // Act
        var notification = new Notification(orderId, type, message, id, createdAt);

        // Assert
        notification.Id.Should().Be(id);
        notification.OrderId.Should().Be(orderId);
        notification.CreatedAt.Should().Be(createdAt);
    }

    [Fact]
    public void Constructor_WithEmptyOrderId_ShouldThrowDomainException()
    {
        // Act
        var act = () => new Notification(Guid.Empty, NotificationType.OrderCreated, "Mensagem");

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("OrderId cannot be empty.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithInvalidMessage_ShouldThrowDomainException(string? invalidMessage)
    {
        // Act
        var act = () => new Notification(Guid.NewGuid(), NotificationType.OrderCreated, invalidMessage!);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("Message is required and cannot be empty.");
    }
}

public class ProcessedMessageTests
{
    [Fact]
    public void Constructor_WithValidParameters_ShouldCreateProcessedMessage()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var eventType = "OrderCreated";

        // Act
        var processedMessage = new ProcessedMessage(eventId, eventType);

        // Assert
        processedMessage.EventId.Should().Be(eventId);
        processedMessage.EventType.Should().Be(eventType);
        processedMessage.ProcessedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Constructor_WithEmptyEventId_ShouldThrowDomainException()
    {
        // Act
        var act = () => new ProcessedMessage(Guid.Empty, "OrderCreated");

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("EventId cannot be empty.");
    }
}
