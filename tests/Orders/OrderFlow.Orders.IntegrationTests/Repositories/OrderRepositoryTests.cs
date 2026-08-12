using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OrderFlow.Orders.Domain.Entities;
using OrderFlow.Orders.Domain.Enums;
using OrderFlow.Orders.Infrastructure.Persistence;
using OrderFlow.Orders.Infrastructure.Persistence.Repositories;
using Xunit;

namespace OrderFlow.Orders.IntegrationTests.Repositories;

public class OrderRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<OrdersDbContext> _dbContextOptions;

    public OrderRepositoryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _dbContextOptions = new DbContextOptionsBuilder<OrdersDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = new OrdersDbContext(_dbContextOptions);
        context.Database.EnsureCreated();
    }

    [Fact]
    public async Task AddAsync_ShouldPersistOrderInDatabase()
    {
        // Arrange
        await using var context = new OrdersDbContext(_dbContextOptions);
        var repository = new OrderRepository(context);
        var order = new Order("Luan Victor", "luan@example.com", 150.00m);

        // Act
        await repository.AddAsync(order);
        await repository.SaveChangesAsync();

        // Assert
        await using var verifyContext = new OrdersDbContext(_dbContextOptions);
        var persistedOrder = await verifyContext.Orders.FirstOrDefaultAsync(o => o.Id == order.Id);

        Assert.NotNull(persistedOrder);
        Assert.Equal(order.Id, persistedOrder.Id);
        Assert.Equal("Luan Victor", persistedOrder.CustomerName);
        Assert.Equal("luan@example.com", persistedOrder.CustomerEmail);
        Assert.Equal(150.00m, persistedOrder.TotalAmount);
        Assert.Equal(OrderStatus.Pending, persistedOrder.Status);
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnOrder_WhenOrderExists()
    {
        // Arrange
        var order = new Order("Maria Silva", "maria@example.com", 299.90m);
        await using (var seedContext = new OrdersDbContext(_dbContextOptions))
        {
            seedContext.Orders.Add(order);
            await seedContext.SaveChangesAsync();
        }

        await using var context = new OrdersDbContext(_dbContextOptions);
        var repository = new OrderRepository(context);

        // Act
        var result = await repository.GetByIdAsync(order.Id);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(order.Id, result.Id);
        Assert.Equal("Maria Silva", result.CustomerName);
        Assert.Equal("maria@example.com", result.CustomerEmail);
    }

    [Fact]
    public async Task GetAllAsync_ShouldReturnAllOrders()
    {
        // Arrange
        var order1 = new Order("Customer 1", "c1@example.com", 50.00m);
        var order2 = new Order("Customer 2", "c2@example.com", 100.00m);

        await using (var seedContext = new OrdersDbContext(_dbContextOptions))
        {
            seedContext.Orders.AddRange(order1, order2);
            await seedContext.SaveChangesAsync();
        }

        await using var context = new OrdersDbContext(_dbContextOptions);
        var repository = new OrderRepository(context);

        // Act
        var results = await repository.GetAllAsync();

        // Assert
        Assert.NotNull(results);
        Assert.Equal(2, results.Count);
        Assert.Contains(results, o => o.Id == order1.Id);
        Assert.Contains(results, o => o.Id == order2.Id);
    }

    [Fact]
    public async Task UpdateOrderStatus_ShouldPersistStatusChangeInDatabase()
    {
        // Arrange
        var order = new Order("João Souza", "joao@example.com", 500.00m);
        await using (var seedContext = new OrdersDbContext(_dbContextOptions))
        {
            seedContext.Orders.Add(order);
            await seedContext.SaveChangesAsync();
        }

        // Act
        await using (var updateContext = new OrdersDbContext(_dbContextOptions))
        {
            var repository = new OrderRepository(updateContext);
            var existingOrder = await repository.GetByIdAsync(order.Id);
            Assert.NotNull(existingOrder);

            existingOrder.StartProcessing();
            await repository.SaveChangesAsync();
        }

        // Assert
        await using var verifyContext = new OrdersDbContext(_dbContextOptions);
        var updatedOrder = await verifyContext.Orders.FirstOrDefaultAsync(o => o.Id == order.Id);

        Assert.NotNull(updatedOrder);
        Assert.Equal(OrderStatus.Processing, updatedOrder.Status);
        Assert.NotNull(updatedOrder.UpdatedAt);
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
