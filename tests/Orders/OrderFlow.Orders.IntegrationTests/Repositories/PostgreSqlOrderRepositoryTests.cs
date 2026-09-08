using Microsoft.EntityFrameworkCore;
using OrderFlow.Orders.Domain.Entities;
using OrderFlow.Orders.Domain.Enums;
using OrderFlow.Orders.Infrastructure.Persistence;
using OrderFlow.Orders.Infrastructure.Persistence.Repositories;
using Testcontainers.PostgreSql;
using Xunit;

namespace OrderFlow.Orders.IntegrationTests.Repositories;

public class PostgreSqlOrderRepositoryTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgresContainer = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("orders_test_db")
        .WithUsername("test_user")
        .WithPassword("test_pass")
        .Build();

    private DbContextOptions<OrdersDbContext> _dbContextOptions = null!;

    public async Task InitializeAsync()
    {
        await _postgresContainer.StartAsync();

        _dbContextOptions = new DbContextOptionsBuilder<OrdersDbContext>()
            .UseNpgsql(_postgresContainer.GetConnectionString())
            .Options;

        await using var context = new OrdersDbContext(_dbContextOptions);
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _postgresContainer.DisposeAsync();
    }

    [Fact]
    public async Task AddAsync_ShouldPersistOrderInPostgreSqlDatabase()
    {
        // Arrange
        await using var context = new OrdersDbContext(_dbContextOptions);
        var repository = new OrderRepository(context);
        var order = new Order("Luan Testcontainers", "luan.pg@example.com", 349.90m);

        // Act
        await repository.AddAsync(order);
        await repository.SaveChangesAsync();

        // Assert
        await using var verifyContext = new OrdersDbContext(_dbContextOptions);
        var persistedOrder = await verifyContext.Orders.FirstOrDefaultAsync(o => o.Id == order.Id);

        Assert.NotNull(persistedOrder);
        Assert.Equal(order.Id, persistedOrder.Id);
        Assert.Equal("Luan Testcontainers", persistedOrder.CustomerName);
        Assert.Equal("luan.pg@example.com", persistedOrder.CustomerEmail);
        Assert.Equal(349.90m, persistedOrder.TotalAmount);
        Assert.Equal(OrderStatus.Pending, persistedOrder.Status);
        Assert.Null(persistedOrder.UpdatedAt);
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnOrder_WhenOrderExistsInPostgreSql()
    {
        // Arrange
        var order = new Order("Maria PG", "maria.pg@example.com", 599.00m);
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
        Assert.Equal("Maria PG", result.CustomerName);
        Assert.Equal("maria.pg@example.com", result.CustomerEmail);
    }

    [Fact]
    public async Task GetAllAsync_ShouldReturnAllOrdersFromPostgreSql()
    {
        // Arrange
        var order1 = new Order("Customer PG 1", "pg1@example.com", 75.00m);
        var order2 = new Order("Customer PG 2", "pg2@example.com", 150.00m);

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
        Assert.Contains(results, o => o.Id == order1.Id);
        Assert.Contains(results, o => o.Id == order2.Id);
    }

    [Fact]
    public async Task UpdateOrderStatus_ShouldPersistStatusChangeInPostgreSql()
    {
        // Arrange
        var order = new Order("Carlos PG", "carlos.pg@example.com", 1200.00m);
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
}
