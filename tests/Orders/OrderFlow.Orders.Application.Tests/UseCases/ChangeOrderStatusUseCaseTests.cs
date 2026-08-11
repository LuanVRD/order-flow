using FluentAssertions;
using FluentValidation;
using NSubstitute;
using OrderFlow.Messaging.Contracts.Events;
using OrderFlow.Orders.Application.DTOs;
using OrderFlow.Orders.Application.Exceptions;
using OrderFlow.Orders.Application.Interfaces;
using OrderFlow.Orders.Application.UseCases;
using OrderFlow.Orders.Application.Validators;
using OrderFlow.Orders.Domain.Entities;
using OrderFlow.Orders.Domain.Enums;
using OrderFlow.Orders.Domain.Exceptions;

namespace OrderFlow.Orders.Application.Tests.UseCases;

public class ChangeOrderStatusUseCaseTests
{
    private readonly IOrderRepository _repository;
    private readonly IEventPublisher _eventPublisher;
    private readonly ChangeOrderStatusUseCase _useCase;

    public ChangeOrderStatusUseCaseTests()
    {
        _repository = Substitute.For<IOrderRepository>();
        _eventPublisher = Substitute.For<IEventPublisher>();
        var validator = new ChangeOrderStatusCommandValidator();
        _useCase = new ChangeOrderStatusUseCase(_repository, _eventPublisher, validator);
    }

    [Fact]
    public async Task ExecuteAsync_WithValidTransitionToProcessing_ShouldUpdateStatusSaveAndPublishEvent()
    {
        // Arrange
        var order = new Order("Alice", "alice@example.com", 100m);
        _repository.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);
        var command = new ChangeOrderStatusCommand(order.Id, OrderStatus.Processing);

        // Act
        var result = await _useCase.ExecuteAsync(command, CancellationToken.None);

        // Assert
        result.Status.Should().Be(OrderStatus.Processing);
        await _repository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _eventPublisher.Received(1).PublishAsync(
            Arg.Is<EventEnvelope<OrderStatusChangedEvent>>(e =>
                e.Data.OrderId == order.Id &&
                e.Data.PreviousStatus == "Pending" &&
                e.Data.NewStatus == "Processing"),
            Arg.Is<string>(rk => rk == "order.status.changed"),
            Arg.Any<CancellationToken>()
        );
    }

    [Fact]
    public async Task ExecuteAsync_WithValidTransitionToCompleted_ShouldPublishStatusChangedAndCompletedEvents()
    {
        // Arrange
        var order = new Order("Alice", "alice@example.com", 100m);
        order.StartProcessing();
        _repository.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);
        var command = new ChangeOrderStatusCommand(order.Id, OrderStatus.Completed);

        // Act
        var result = await _useCase.ExecuteAsync(command, CancellationToken.None);

        // Assert
        result.Status.Should().Be(OrderStatus.Completed);
        await _repository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _eventPublisher.Received(1).PublishAsync(
            Arg.Is<EventEnvelope<OrderStatusChangedEvent>>(e => e.Data.NewStatus == "Completed"),
            Arg.Is<string>(rk => rk == "order.status.changed"),
            Arg.Any<CancellationToken>()
        );
        await _eventPublisher.Received(1).PublishAsync(
            Arg.Is<EventEnvelope<OrderCompletedEvent>>(e => e.Data.OrderId == order.Id),
            Arg.Is<string>(rk => rk == "order.completed"),
            Arg.Any<CancellationToken>()
        );
    }

    [Fact]
    public async Task ExecuteAsync_WhenOrderNotFound_ShouldThrowNotFoundException()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();
        _repository.GetByIdAsync(nonExistentId, Arg.Any<CancellationToken>()).Returns((Order?)null);
        var command = new ChangeOrderStatusCommand(nonExistentId, OrderStatus.Processing);

        // Act
        var act = async () => await _useCase.ExecuteAsync(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>();
        await _repository.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WithInvalidTransition_ShouldThrowDomainExceptionAndNotSave()
    {
        // Arrange
        var order = new Order("Alice", "alice@example.com", 100m);
        // Direct transition from Pending -> Completed is invalid in domain
        _repository.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);
        var command = new ChangeOrderStatusCommand(order.Id, OrderStatus.Completed);

        // Act
        var act = async () => await _useCase.ExecuteAsync(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<DomainException>();
        await _repository.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WithEmptyGuid_ShouldThrowValidationException()
    {
        // Arrange
        var command = new ChangeOrderStatusCommand(Guid.Empty, OrderStatus.Processing);

        // Act
        var act = async () => await _useCase.ExecuteAsync(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ValidationException>();
    }
}
