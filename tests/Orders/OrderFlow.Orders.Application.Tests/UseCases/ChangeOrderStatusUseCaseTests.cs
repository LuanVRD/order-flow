using FluentAssertions;
using FluentValidation;
using NSubstitute;
using OrderFlow.Orders.Application.DTOs;
using OrderFlow.Orders.Application.Exceptions;
using OrderFlow.Orders.Application.Interfaces;
using OrderFlow.Orders.Application.UseCases;
using OrderFlow.Orders.Application.Validators;
using OrderFlow.Orders.Domain.Entities;
using OrderFlow.Orders.Domain.Enums;
using OrderFlow.Orders.Domain.Events;
using OrderFlow.Orders.Domain.Exceptions;

namespace OrderFlow.Orders.Application.Tests.UseCases;

public class ChangeOrderStatusUseCaseTests
{
    private readonly IOrderRepository _repository;
    private readonly ChangeOrderStatusUseCase _useCase;

    public ChangeOrderStatusUseCaseTests()
    {
        _repository = Substitute.For<IOrderRepository>();
        var validator = new ChangeOrderStatusCommandValidator();
        _useCase = new ChangeOrderStatusUseCase(_repository, validator);
    }

    [Fact]
    public async Task ExecuteAsync_WithValidTransitionToProcessing_ShouldUpdateStatusAndSave()
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
        order.DomainEvents.Should().Contain(e => e is OrderStatusChangedDomainEvent);
    }

    [Fact]
    public async Task ExecuteAsync_WithValidTransitionToCompleted_ShouldUpdateStatusAndEmitDomainEvents()
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
        order.DomainEvents.Should().Contain(e => e is OrderStatusChangedDomainEvent);
        order.DomainEvents.Should().Contain(e => e is OrderCompletedDomainEvent);
    }

    [Fact]
    public async Task ExecuteAsync_WithValidTransitionToCancelled_ShouldUpdateStatusAndEmitDomainEvents()
    {
        // Arrange
        var order = new Order("Alice", "alice@example.com", 100m);
        _repository.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);
        var command = new ChangeOrderStatusCommand(order.Id, OrderStatus.Cancelled);

        // Act
        var result = await _useCase.ExecuteAsync(command, CancellationToken.None);

        // Assert
        result.Status.Should().Be(OrderStatus.Cancelled);
        await _repository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        order.DomainEvents.Should().Contain(e => e is OrderStatusChangedDomainEvent);
        order.DomainEvents.Should().Contain(e => e is OrderCancelledDomainEvent);
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
