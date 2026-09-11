using FluentAssertions;
using FluentValidation;
using NSubstitute;
using OrderFlow.Orders.Application.DTOs;
using OrderFlow.Orders.Application.Interfaces;
using OrderFlow.Orders.Application.UseCases;
using OrderFlow.Orders.Application.Validators;
using OrderFlow.Orders.Domain.Entities;
using OrderFlow.Orders.Domain.Enums;
using OrderFlow.Orders.Domain.Events;

namespace OrderFlow.Orders.Application.Tests.UseCases;

public class CreateOrderUseCaseTests
{
    private readonly IOrderRepository _repository;
    private readonly CreateOrderUseCase _useCase;

    public CreateOrderUseCaseTests()
    {
        _repository = Substitute.For<IOrderRepository>();
        var validator = new CreateOrderCommandValidator();
        _useCase = new CreateOrderUseCase(_repository, validator);
    }

    [Fact]
    public async Task ExecuteAsync_WithValidCommand_ShouldCreateOrderAndPersist()
    {
        // Arrange
        var command = new CreateOrderCommand("John Doe", "john.doe@example.com", 150.00m);
        Order? capturedOrder = null;
        await _repository.AddAsync(Arg.Do<Order>(o => capturedOrder = o), Arg.Any<CancellationToken>());

        // Act
        var result = await _useCase.ExecuteAsync(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().NotBeEmpty();
        result.CustomerName.Should().Be("John Doe");
        result.CustomerEmail.Should().Be("john.doe@example.com");
        result.TotalAmount.Should().Be(150.00m);
        result.Status.Should().Be(OrderStatus.Pending);

        await _repository.Received(1).AddAsync(Arg.Is<Order>(o => o.Id == result.Id), Arg.Any<CancellationToken>());
        await _repository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());

        capturedOrder.Should().NotBeNull();
        capturedOrder!.DomainEvents.Should().ContainSingle(e => e is OrderCreatedDomainEvent);
        var domainEvent = capturedOrder.DomainEvents.First() as OrderCreatedDomainEvent;
        domainEvent!.OrderId.Should().Be(result.Id);
        domainEvent.CustomerName.Should().Be("John Doe");
        domainEvent.CustomerEmail.Should().Be("john.doe@example.com");
    }

    [Theory]
    [InlineData("", "john.doe@example.com", 100.0)]
    [InlineData("John Doe", "invalid-email", 100.0)]
    [InlineData("John Doe", "john.doe@example.com", 0.0)]
    [InlineData("John Doe", "john.doe@example.com", -50.0)]
    public async Task ExecuteAsync_WithInvalidCommand_ShouldThrowValidationExceptionAndNotCallRepository(
        string customerName, string customerEmail, decimal totalAmount)
    {
        // Arrange
        var command = new CreateOrderCommand(customerName, customerEmail, totalAmount);

        // Act
        var act = async () => await _useCase.ExecuteAsync(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ValidationException>();
        await _repository.DidNotReceive().AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
        await _repository.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
