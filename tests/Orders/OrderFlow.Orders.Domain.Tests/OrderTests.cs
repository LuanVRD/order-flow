using OrderFlow.Orders.Domain.Entities;
using OrderFlow.Orders.Domain.Enums;
using OrderFlow.Orders.Domain.Events;
using OrderFlow.Orders.Domain.Exceptions;

namespace OrderFlow.Orders.Domain.Tests;

public class OrderTests
{
    [Fact]
    public void CreateOrder_WithValidParameters_ShouldInitializeCorrectly()
    {
        // Arrange
        var customerName = "John Doe";
        var customerEmail = "john.doe@example.com";
        var totalAmount = 150.50m;
        var beforeCreation = DateTimeOffset.UtcNow;

        // Act
        var order = new Order(customerName, customerEmail, totalAmount);
        var afterCreation = DateTimeOffset.UtcNow;

        // Assert
        Assert.NotEqual(Guid.Empty, order.Id);
        Assert.Equal(customerName, order.CustomerName);
        Assert.Equal(customerEmail, order.CustomerEmail);
        Assert.Equal(totalAmount, order.TotalAmount);
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Null(order.UpdatedAt);
        Assert.True(order.CreatedAt >= beforeCreation && order.CreatedAt <= afterCreation);
        Assert.Equal(TimeSpan.Zero, order.CreatedAt.Offset); // Ensures UTC offset
    }

    [Fact]
    public void CreateOrder_WithValidParameters_ShouldRegisterOrderCreatedDomainEvent()
    {
        // Arrange & Act
        var order = new Order("John Doe", "john.doe@example.com", 150.50m);

        // Assert
        Assert.Single(order.DomainEvents);
        var domainEvent = Assert.IsType<OrderCreatedDomainEvent>(order.DomainEvents.Single());
        Assert.Equal(order.Id, domainEvent.OrderId);
        Assert.Equal("John Doe", domainEvent.CustomerName);
        Assert.Equal("john.doe@example.com", domainEvent.CustomerEmail);
        Assert.Equal(150.50m, domainEvent.TotalAmount);
        Assert.Equal(OrderStatus.Pending, domainEvent.Status);
        Assert.Equal(order.CreatedAt, domainEvent.OccurredOn);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void CreateOrder_WithInvalidCustomerName_ShouldThrowDomainException(string? invalidName)
    {
        // Act & Assert
        var exception = Assert.Throws<DomainException>(() =>
            new Order(invalidName!, "john.doe@example.com", 100m));

        Assert.Contains("Customer name is required", exception.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("invalid-email")]
    [InlineData("@example.com")]
    [InlineData("john.doe@")]
    public void CreateOrder_WithInvalidCustomerEmail_ShouldThrowDomainException(string? invalidEmail)
    {
        // Act & Assert
        var exception = Assert.Throws<DomainException>(() =>
            new Order("John Doe", invalidEmail!, 100m));

        Assert.Contains("valid customer email address", exception.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100.50)]
    public void CreateOrder_WithZeroOrNegativeAmount_ShouldThrowDomainException(decimal invalidAmount)
    {
        // Act & Assert
        var exception = Assert.Throws<DomainException>(() =>
            new Order("John Doe", "john.doe@example.com", invalidAmount));

        Assert.Contains("greater than zero", exception.Message);
    }

    [Fact]
    public void ChangeStatus_PendingToProcessing_ShouldSucceedAndUpdateTimestamp()
    {
        // Arrange
        var order = new Order("John Doe", "john.doe@example.com", 100m);

        // Act
        order.StartProcessing();

        // Assert
        Assert.Equal(OrderStatus.Processing, order.Status);
        Assert.NotNull(order.UpdatedAt);
        Assert.Equal(TimeSpan.Zero, order.UpdatedAt.Value.Offset);
    }

    [Fact]
    public void StartProcessing_ShouldRegisterOrderStatusChangedDomainEvent()
    {
        // Arrange
        var order = new Order("John Doe", "john.doe@example.com", 100m);
        order.ClearDomainEvents();

        // Act
        order.StartProcessing();

        // Assert
        Assert.Single(order.DomainEvents);
        var domainEvent = Assert.IsType<OrderStatusChangedDomainEvent>(order.DomainEvents.Single());
        Assert.Equal(order.Id, domainEvent.OrderId);
        Assert.Equal(OrderStatus.Pending, domainEvent.PreviousStatus);
        Assert.Equal(OrderStatus.Processing, domainEvent.NewStatus);
        Assert.Equal(order.UpdatedAt, domainEvent.OccurredOn);
    }

    [Fact]
    public void ChangeStatus_ProcessingToCompleted_ShouldSucceedAndUpdateTimestamp()
    {
        // Arrange
        var order = new Order("John Doe", "john.doe@example.com", 100m);
        order.StartProcessing();

        // Act
        order.Complete();

        // Assert
        Assert.Equal(OrderStatus.Completed, order.Status);
        Assert.NotNull(order.UpdatedAt);
    }

    [Fact]
    public void Complete_ShouldRegisterOrderStatusChangedAndOrderCompletedDomainEvents()
    {
        // Arrange
        var order = new Order("John Doe", "john.doe@example.com", 100m);
        order.StartProcessing();
        order.ClearDomainEvents();

        // Act
        order.Complete();

        // Assert
        Assert.Equal(2, order.DomainEvents.Count);
        var statusChangedEvent = Assert.IsType<OrderStatusChangedDomainEvent>(order.DomainEvents.First());
        var completedEvent = Assert.IsType<OrderCompletedDomainEvent>(order.DomainEvents.Last());

        Assert.Equal(order.Id, statusChangedEvent.OrderId);
        Assert.Equal(OrderStatus.Processing, statusChangedEvent.PreviousStatus);
        Assert.Equal(OrderStatus.Completed, statusChangedEvent.NewStatus);

        Assert.Equal(order.Id, completedEvent.OrderId);
        Assert.Equal(order.UpdatedAt, completedEvent.OccurredOn);
    }

    [Fact]
    public void ChangeStatus_PendingToCancelled_ShouldSucceedAndUpdateTimestamp()
    {
        // Arrange
        var order = new Order("John Doe", "john.doe@example.com", 100m);

        // Act
        order.Cancel();

        // Assert
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.NotNull(order.UpdatedAt);
    }

    [Fact]
    public void Cancel_ShouldRegisterOrderStatusChangedAndOrderCancelledDomainEvents()
    {
        // Arrange
        var order = new Order("John Doe", "john.doe@example.com", 100m);
        order.ClearDomainEvents();

        // Act
        order.Cancel();

        // Assert
        Assert.Equal(2, order.DomainEvents.Count);
        var statusChangedEvent = Assert.IsType<OrderStatusChangedDomainEvent>(order.DomainEvents.First());
        var cancelledEvent = Assert.IsType<OrderCancelledDomainEvent>(order.DomainEvents.Last());

        Assert.Equal(order.Id, statusChangedEvent.OrderId);
        Assert.Equal(OrderStatus.Pending, statusChangedEvent.PreviousStatus);
        Assert.Equal(OrderStatus.Cancelled, statusChangedEvent.NewStatus);

        Assert.Equal(order.Id, cancelledEvent.OrderId);
        Assert.Equal(OrderStatus.Pending, cancelledEvent.PreviousStatus);
        Assert.Equal(order.UpdatedAt, cancelledEvent.OccurredOn);
    }

    [Fact]
    public void ChangeStatus_ProcessingToCancelled_ShouldSucceedAndUpdateTimestamp()
    {
        // Arrange
        var order = new Order("John Doe", "john.doe@example.com", 100m);
        order.StartProcessing();

        // Act
        order.Cancel();

        // Assert
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.NotNull(order.UpdatedAt);
    }

    [Fact]
    public void ChangeStatus_CompletedToCancelled_ShouldThrowDomainExceptionAndNotRegisterNewEvents()
    {
        // Arrange
        var order = new Order("John Doe", "john.doe@example.com", 100m);
        order.StartProcessing();
        order.Complete();
        var initialEventCount = order.DomainEvents.Count;

        // Act & Assert
        var exception = Assert.Throws<DomainException>(() => order.Cancel());
        Assert.Contains("Cannot transition order status", exception.Message);
        Assert.Equal(initialEventCount, order.DomainEvents.Count);
    }

    [Theory]
    [InlineData(OrderStatus.Pending)]
    [InlineData(OrderStatus.Processing)]
    [InlineData(OrderStatus.Completed)]
    [InlineData(OrderStatus.Cancelled)]
    public void ChangeStatus_FromCancelledToAnyStatus_ShouldThrowDomainExceptionAndNotRegisterNewEvents(OrderStatus targetStatus)
    {
        // Arrange
        var order = new Order("John Doe", "john.doe@example.com", 100m);
        order.Cancel();
        var initialEventCount = order.DomainEvents.Count;

        // Act & Assert
        Assert.Throws<DomainException>(() => order.ChangeStatus(targetStatus));
        Assert.Equal(initialEventCount, order.DomainEvents.Count);
    }

    [Theory]
    [InlineData(OrderStatus.Pending)]
    [InlineData(OrderStatus.Processing)]
    public void ChangeStatus_FromCompletedToPreviousStatuses_ShouldThrowDomainException(OrderStatus targetStatus)
    {
        // Arrange
        var order = new Order("John Doe", "john.doe@example.com", 100m);
        order.StartProcessing();
        order.Complete();

        // Act & Assert
        Assert.Throws<DomainException>(() => order.ChangeStatus(targetStatus));
    }

    [Fact]
    public void ChangeStatus_ProcessingToPending_ShouldThrowDomainException()
    {
        // Arrange
        var order = new Order("John Doe", "john.doe@example.com", 100m);
        order.StartProcessing();

        // Act & Assert
        var exception = Assert.Throws<DomainException>(() => order.ChangeStatus(OrderStatus.Pending));
        Assert.Contains("Cannot transition order status", exception.Message);
    }

    [Theory]
    [InlineData(OrderStatus.Pending)]
    [InlineData(OrderStatus.Processing)]
    public void ChangeStatus_ToSameStatus_ShouldThrowDomainException(OrderStatus currentStatus)
    {
        // Arrange
        var order = new Order("John Doe", "john.doe@example.com", 100m);
        if (currentStatus == OrderStatus.Processing)
        {
            order.StartProcessing();
        }

        // Act & Assert
        var exception = Assert.Throws<DomainException>(() => order.ChangeStatus(currentStatus));
        Assert.Contains("already in", exception.Message);
    }

    [Fact]
    public void ClearDomainEvents_ShouldRemoveAllRegisteredEvents()
    {
        // Arrange
        var order = new Order("John Doe", "john.doe@example.com", 100m);
        Assert.NotEmpty(order.DomainEvents);

        // Act
        order.ClearDomainEvents();

        // Assert
        Assert.Empty(order.DomainEvents);
    }
}

