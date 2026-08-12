using Microsoft.EntityFrameworkCore;
using OrderFlow.Orders.Domain.Entities;

namespace OrderFlow.Orders.Infrastructure.Persistence;

public class OrdersDbContext : DbContext
{
    public DbSet<Order> Orders => Set<Order>();

    public OrdersDbContext(DbContextOptions<OrdersDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrdersDbContext).Assembly);
    }
}
