using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OrderFlow.Orders.Api.Models.Requests;
using OrderFlow.Orders.Application.DTOs;
using OrderFlow.Orders.Domain.Enums;
using OrderFlow.Orders.Infrastructure.Persistence;

namespace OrderFlow.Orders.IntegrationTests.Controllers;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _sqliteConnection;

    public CustomWebApplicationFactory()
    {
        _sqliteConnection = new SqliteConnection("DataSource=:memory:");
        _sqliteConnection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:DefaultConnection", "DataSource=:memory:");

        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<OrdersDbContext>));
            if (descriptor != null)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<OrdersDbContext>(options =>
            {
                options.UseSqlite(_sqliteConnection);
            });

            var publisherDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(OrderFlow.Orders.Application.Interfaces.IEventPublisher));
            if (publisherDescriptor != null)
            {
                services.Remove(publisherDescriptor);
            }
            services.AddScoped<OrderFlow.Orders.Application.Interfaces.IEventPublisher, OrderFlow.Orders.Infrastructure.Messaging.LoggingEventPublisher>();

            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            db.Database.EnsureCreated();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _sqliteConnection.Dispose();
        }
    }
}

public class OrdersControllerTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions;

    public OrdersControllerTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };
    }

    [Fact]
    public async Task CreateOrder_ShouldReturn201Created_WhenCommandIsValid()
    {
        // Arrange
        var command = new CreateOrderCommand("Luan Victor", "luan@example.com", 250.00m);

        // Act
        var response = await _client.PostAsJsonAsync("/api/orders", command, _jsonOptions);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<OrderResponse>(_jsonOptions);
        Assert.NotNull(body);
        Assert.NotEqual(Guid.Empty, body.Id);
        Assert.Equal("Luan Victor", body.CustomerName);
        Assert.Equal("luan@example.com", body.CustomerEmail);
        Assert.Equal(250.00m, body.TotalAmount);
        Assert.Equal(OrderStatus.Pending, body.Status);
        Assert.NotNull(response.Headers.Location);
    }

    [Fact]
    public async Task CreateOrder_ShouldReturn400BadRequest_WhenCommandIsInvalid()
    {
        // Arrange
        var command = new CreateOrderCommand("", "invalid-email", -50.00m);

        // Act
        var response = await _client.PostAsJsonAsync("/api/orders", command, _jsonOptions);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetOrders_ShouldReturn200OK_WithListOfOrders()
    {
        // Arrange
        var command = new CreateOrderCommand("Maria", "maria@example.com", 100.00m);
        await _client.PostAsJsonAsync("/api/orders", command, _jsonOptions);

        // Act
        var response = await _client.GetAsync("/api/orders");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var orders = await response.Content.ReadFromJsonAsync<List<OrderResponse>>(_jsonOptions);
        Assert.NotNull(orders);
        Assert.NotEmpty(orders);
    }

    [Fact]
    public async Task GetOrderById_ShouldReturn200OK_WhenOrderExists()
    {
        // Arrange
        var createResponse = await _client.PostAsJsonAsync("/api/orders", new CreateOrderCommand("Carlos", "carlos@example.com", 300.00m), _jsonOptions);
        var createdOrder = await createResponse.Content.ReadFromJsonAsync<OrderResponse>(_jsonOptions);
        Assert.NotNull(createdOrder);

        // Act
        var response = await _client.GetAsync($"/api/orders/{createdOrder.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var order = await response.Content.ReadFromJsonAsync<OrderResponse>(_jsonOptions);
        Assert.NotNull(order);
        Assert.Equal(createdOrder.Id, order.Id);
    }

    [Fact]
    public async Task GetOrderById_ShouldReturn404NotFound_WhenOrderDoesNotExist()
    {
        // Act
        var response = await _client.GetAsync($"/api/orders/{Guid.NewGuid()}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateStatus_ShouldReturn200OK_WhenStatusIsUpdated()
    {
        // Arrange
        var createResponse = await _client.PostAsJsonAsync("/api/orders", new CreateOrderCommand("Ana", "ana@example.com", 150.00m), _jsonOptions);
        var createdOrder = await createResponse.Content.ReadFromJsonAsync<OrderResponse>(_jsonOptions);
        Assert.NotNull(createdOrder);

        var updateRequest = new UpdateOrderStatusRequest(OrderStatus.Processing);

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/orders/{createdOrder.Id}/status", updateRequest, _jsonOptions);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updatedOrder = await response.Content.ReadFromJsonAsync<OrderResponse>(_jsonOptions);
        Assert.NotNull(updatedOrder);
        Assert.Equal(OrderStatus.Processing, updatedOrder.Status);
    }

    [Fact]
    public async Task CancelOrder_ShouldReturn200OK_WhenOrderIsCancelled()
    {
        // Arrange
        var createResponse = await _client.PostAsJsonAsync("/api/orders", new CreateOrderCommand("Pedro", "pedro@example.com", 80.00m), _jsonOptions);
        var createdOrder = await createResponse.Content.ReadFromJsonAsync<OrderResponse>(_jsonOptions);
        Assert.NotNull(createdOrder);

        // Act
        var response = await _client.PostAsync($"/api/orders/{createdOrder.Id}/cancel", null);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cancelledOrder = await response.Content.ReadFromJsonAsync<OrderResponse>(_jsonOptions);
        Assert.NotNull(cancelledOrder);
        Assert.Equal(OrderStatus.Cancelled, cancelledOrder.Status);
    }
}
