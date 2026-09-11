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

public class CancelOrderUseCaseTests
{
    private readonly IOrderRepository _repository;
    private readonly CancelOrderUseCase _useCase;

    public CancelOrderUseCaseTests()
    {
        _repository = Substitute.For<IOrderRepository>();
        var validator = new CancelOrderCommandValidator();
        _useCase = new CancelOrderUseCase(_repository, validator);
    }

    [Fact]
    public async Task ExecuteAsync_WithPendingOrder_ShouldCancelOrderSaveAndEmitDomainEvents()
    {
        // Arrange
        var order = new Order("Bob", "bob@example.com", 300m);
        _repository.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);
        var command = new CancelOrderCommand(order.Id);

        // Act
        var result = await _useCase.ExecuteAsync(command, CancellationToken.None);

        // Assert
        result.Status.Should().Be(OrderStatus.Cancelled);
        await _repository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        order.DomainEvents.Should().Contain(e => e is OrderStatusChangedDomainEvent);
        order.DomainEvents.Should().Contain(e => e is OrderCancelledDomainEvent);
    }

    [Fact]
    public async Task ExecuteAsync_WithProcessingOrder_ShouldCancelOrderSaveAndEmitDomainEvents()
    {
        // Arrange
        var order = new Order("Bob", "bob@example.com", 300m);
        order.StartProcessing();
        _repository.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);
        var command = new CancelOrderCommand(order.Id);

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
        var command = new CancelOrderCommand(nonExistentId);

        // Act
        var act = async () => await _useCase.ExecuteAsync(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>();
        await _repository.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WithCompletedOrder_ShouldThrowDomainExceptionAndNotSave()
    {
        // Arrange
        var order = new Order("Bob", "bob@example.com", 300m);
        order.StartProcessing();
        order.Complete();
        _repository.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);
        var command = new CancelOrderCommand(order.Id);

        // Act
        var act = async () => await _useCase.ExecuteAsync(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("Cannot transition order status from 'Completed' to 'Cancelled'.");
        await _repository.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WithEmptyGuid_ShouldThrowValidationException()
    {
        // Arrange
        var command = new CancelOrderCommand(Guid.Empty);

        // Act
        var act = async () => await _useCase.ExecuteAsync(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ValidationException>();
    }
}
