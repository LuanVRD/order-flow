using FluentAssertions;
using NSubstitute;
using OrderFlow.Orders.Application.Interfaces;
using OrderFlow.Orders.Application.UseCases;
using OrderFlow.Orders.Domain.Entities;

namespace OrderFlow.Orders.Application.Tests.UseCases;

public class GetOrdersUseCaseTests
{
    private readonly IOrderRepository _repository;
    private readonly GetOrdersUseCase _useCase;

    public GetOrdersUseCaseTests()
    {
        _repository = Substitute.For<IOrderRepository>();
        _useCase = new GetOrdersUseCase(_repository);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnAllOrders()
    {
        // Arrange
        var orders = new List<Order>
        {
            new("Alice", "alice@example.com", 100m),
            new("Bob", "bob@example.com", 200m)
        };
        _repository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(orders);

        // Act
        var result = await _useCase.ExecuteAsync(CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        result.Select(r => r.CustomerName).Should().Contain(new[] { "Alice", "Bob" });
    }

    [Fact]
    public async Task ExecuteAsync_WhenNoOrdersExist_ShouldReturnEmptyCollection()
    {
        // Arrange
        _repository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<Order>());

        // Act
        var result = await _useCase.ExecuteAsync(CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }
}
