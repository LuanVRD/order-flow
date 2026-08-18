using FluentAssertions;
using FluentValidation;
using NSubstitute;
using OrderFlow.Messaging.Contracts.Events;
using OrderFlow.Orders.Application.DTOs;
using OrderFlow.Orders.Application.Interfaces;
using OrderFlow.Orders.Application.UseCases;
using OrderFlow.Orders.Application.Validators;
using OrderFlow.Orders.Domain.Entities;
using OrderFlow.Orders.Domain.Enums;

namespace OrderFlow.Orders.Application.Tests.UseCases;

public class CreateOrderUseCaseTests
{
    private readonly IOrderRepository _repository;
    private readonly IEventPublisher _eventPublisher;
    private readonly CreateOrderUseCase _useCase;

    public CreateOrderUseCaseTests()
    {
        _repository = Substitute.For<IOrderRepository>();
        _eventPublisher = Substitute.For<IEventPublisher>();
        var validator = new CreateOrderCommandValidator();
        _useCase = new CreateOrderUseCase(_repository, _eventPublisher, validator);
    }

    [Fact]
    public async Task ExecuteAsync_WithValidCommand_ShouldCreateOrderPersistAndPublishEvent()
    {
        // Arrange
        var command = new CreateOrderCommand("John Doe", "john.doe@example.com", 150.00m);

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
        await _eventPublisher.Received(1).PublishAsync(
            Arg.Is<EventEnvelope<OrderCreatedIntegrationEvent>>(e => e.Data.OrderId == result.Id && e.EventType == "OrderCreated"),
            Arg.Is<string>(rk => rk == "order.created"),
            Arg.Any<CancellationToken>()
        );
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
        await _eventPublisher.DidNotReceive().PublishAsync(
            Arg.Any<EventEnvelope<OrderCreatedIntegrationEvent>>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
