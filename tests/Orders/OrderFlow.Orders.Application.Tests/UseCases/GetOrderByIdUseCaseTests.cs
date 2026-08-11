using FluentAssertions;
using NSubstitute;
using OrderFlow.Orders.Application.Exceptions;
using OrderFlow.Orders.Application.Interfaces;
using OrderFlow.Orders.Application.UseCases;
using OrderFlow.Orders.Domain.Entities;

namespace OrderFlow.Orders.Application.Tests.UseCases;

public class GetOrderByIdUseCaseTests
{
    private readonly IOrderRepository _repository;
    private readonly GetOrderByIdUseCase _useCase;

    public GetOrderByIdUseCaseTests()
    {
        _repository = Substitute.For<IOrderRepository>();
        _useCase = new GetOrderByIdUseCase(_repository);
    }

    [Fact]
    public async Task ExecuteAsync_WhenOrderExists_ShouldReturnOrderResponse()
    {
        // Arrange
        var order = new Order("Jane Doe", "jane@example.com", 250.00m);
        _repository.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        // Act
        var result = await _useCase.ExecuteAsync(order.Id, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(order.Id);
        result.CustomerName.Should().Be("Jane Doe");
        result.CustomerEmail.Should().Be("jane@example.com");
        result.TotalAmount.Should().Be(250.00m);
    }

    [Fact]
    public async Task ExecuteAsync_WhenOrderDoesNotExist_ShouldThrowNotFoundException()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();
        _repository.GetByIdAsync(nonExistentId, Arg.Any<CancellationToken>()).Returns((Order?)null);

        // Act
        var act = async () => await _useCase.ExecuteAsync(nonExistentId, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage($"Entity 'Order' ({nonExistentId}) was not found.");
    }
}
