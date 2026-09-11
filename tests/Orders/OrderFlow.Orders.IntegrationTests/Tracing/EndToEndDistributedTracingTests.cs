using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using OrderFlow.Messaging.Contracts.Correlation;
using OrderFlow.Messaging.Contracts.Events;
using OrderFlow.Notifications.Application;
using OrderFlow.Notifications.Infrastructure.Messaging;
using OrderFlow.Notifications.Infrastructure.Persistence;
using OrderFlow.Notifications.Infrastructure.Persistence.Repositories;
using OrderFlow.Orders.Api.Models.Requests;
using OrderFlow.Orders.Application.DTOs;
using OrderFlow.Orders.Infrastructure.Messaging;
using OrderFlow.Orders.Infrastructure.Outbox;
using OrderFlow.Orders.Infrastructure.Persistence;
using OrderFlow.Orders.Infrastructure.Persistence.Interceptors;
using RabbitMQ.Client;

namespace OrderFlow.Orders.IntegrationTests.Tracing;

public class CapturingEventPublisher : IEventPublisher
{
    private readonly object _lock = new();
    public List<(object Message, string RoutingKey)> PublishedMessages { get; } = new();

    public Task PublishAsync<T>(T message, string routingKey, CancellationToken cancellationToken = default) where T : class
    {
        lock (_lock)
        {
            PublishedMessages.Add((message, routingKey));
        }
        return Task.CompletedTask;
    }
}

public class TracingWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _sqliteConnection;
    public CapturingEventPublisher EventPublisher { get; } = new();

    public TracingWebApplicationFactory()
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

            services.AddDbContext<OrdersDbContext>((sp, options) =>
            {
                var interceptor = sp.GetRequiredService<OutboxSaveChangesInterceptor>();
                options.AddInterceptors(interceptor);
                options.UseSqlite(_sqliteConnection);
            });

            var publisherDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IEventPublisher));
            if (publisherDescriptor != null)
            {
                services.Remove(publisherDescriptor);
            }
            services.AddSingleton<IEventPublisher>(EventPublisher);

            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            db.Database.EnsureCreated();
        });
    }

    public async Task ProcessOutboxMessagesAsync()
    {
        using var scope = Services.CreateScope();
        var processor = scope.ServiceProvider.GetServices<IHostedService>()
            .OfType<OutboxProcessorBackgroundService>()
            .FirstOrDefault();
        if (processor != null)
        {
            await processor.ProcessPendingBatchAsync(CancellationToken.None);
        }

        for (int i = 0; i < 20; i++)
        {
            lock (EventPublisher)
            {
                if (EventPublisher.PublishedMessages.Count > 0)
                {
                    break;
                }
            }
            await Task.Delay(50);
        }
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

public class EndToEndDistributedTracingTests : IClassFixture<TracingWebApplicationFactory>, IDisposable
{
    private readonly TracingWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly SqliteConnection _notificationsDbConnection;
    private readonly ServiceProvider _notificationsServiceProvider;
    private readonly OrderEventsConsumer _notificationsConsumer;
    private readonly IChannel _rabbitMqChannelMock;

    public EndToEndDistributedTracingTests(TracingWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        // Setup Notifications in-memory stack
        _notificationsDbConnection = new SqliteConnection("DataSource=:memory:");
        _notificationsDbConnection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNotificationsApplication();
        services.AddSingleton<ICorrelationContextAccessor, CorrelationContextAccessor>();

        services.AddDbContext<NotificationsDbContext>(options =>
            options.UseSqlite(_notificationsDbConnection));

        services.AddScoped<OrderFlow.Notifications.Application.Interfaces.INotificationRepository, NotificationRepository>();
        services.AddScoped<OrderFlow.Notifications.Application.Interfaces.IProcessedMessageRepository, ProcessedMessageRepository>();
        services.AddScoped<OrderFlow.Notifications.Application.Interfaces.IUnitOfWork, UnitOfWork>();

        var options = new OrderFlow.Notifications.Infrastructure.Messaging.RabbitMqOptions
        {
            HostName = "localhost",
            Port = 5672,
            ExchangeName = "orderflow.orders",
            QueueName = "orderflow.notifications"
        };
        services.AddSingleton(Options.Create(options));

        var connectionMock = Substitute.For<OrderFlow.Notifications.Infrastructure.Messaging.IRabbitMqConnection>();
        services.AddSingleton(connectionMock);

        _rabbitMqChannelMock = Substitute.For<IChannel>();

        var loggerMock = Substitute.For<ILogger<OrderEventsConsumer>>();
        services.AddSingleton(loggerMock);

        services.AddSingleton<OrderEventsConsumer>();

        _notificationsServiceProvider = services.BuildServiceProvider();

        using var scope = _notificationsServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        db.Database.EnsureCreated();

        _notificationsConsumer = _notificationsServiceProvider.GetRequiredService<OrderEventsConsumer>();
    }

    [Fact]
    public async Task CompleteTraceChain_FromHttpRequestToRabbitMqToNotificationProcessing_ShouldPreserveCorrelationId()
    {
        // 1. Arrange: Client sends HTTP request with explicit X-Correlation-ID
        var clientCorrelationId = $"e2e-trace-{Guid.NewGuid():N}";
        var command = new CreateOrderCommand("Carlos Silva", "carlos.silva@example.com", 450.00m);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders")
        {
            Content = JsonContent.Create(command, options: _jsonOptions)
        };
        request.Headers.Add(CorrelationConstants.HeaderName, clientCorrelationId);

        // 2. Act: Orders API processes HTTP request
        var response = await _client.SendAsync(request);

        // 3. Assert: Orders API returns 201 and identical X-Correlation-ID in response header
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Contains(CorrelationConstants.HeaderName).Should().BeTrue();
        response.Headers.GetValues(CorrelationConstants.HeaderName).First().Should().Be(clientCorrelationId);

        var orderResponse = await response.Content.ReadFromJsonAsync<OrderResponse>(_jsonOptions);
        orderResponse.Should().NotBeNull();
        orderResponse!.Id.Should().NotBeEmpty();

        // Process Outbox to trigger publication
        await _factory.ProcessOutboxMessagesAsync();

        // 4. Assert: Event published to RabbitMQ contains the exact same Correlation ID
        _factory.EventPublisher.PublishedMessages.Should().NotBeEmpty();
        var publishedItem = _factory.EventPublisher.PublishedMessages
            .Last(m => m.Message is EventEnvelope<OrderCreatedIntegrationEvent> env && env.Data.OrderId == orderResponse.Id);

        var envelope = (EventEnvelope<OrderCreatedIntegrationEvent>)publishedItem.Message;
        envelope.CorrelationId.Should().Be(clientCorrelationId);
        envelope.Data.OrderId.Should().Be(orderResponse.Id);
        publishedItem.RoutingKey.Should().Be("order.created");

        // 5. Act: Notifications Service consumes the published event message
        var eventJson = JsonSerializer.Serialize(envelope);
        var messageBody = Encoding.UTF8.GetBytes(eventJson);

        var amqpPropsMock = Substitute.For<IReadOnlyBasicProperties>();
        amqpPropsMock.Type.Returns("OrderCreated");
        amqpPropsMock.CorrelationId.Returns(clientCorrelationId);
        amqpPropsMock.MessageId.Returns(envelope.EventId.ToString());

        await _notificationsConsumer.ProcessMessageAsync(
            messageBody,
            amqpPropsMock,
            deliveryTag: 100UL,
            _rabbitMqChannelMock,
            CancellationToken.None);

        // 6. Assert: Notifications Service acknowledged the message and persisted the notification
        await _rabbitMqChannelMock.Received(1).BasicAckAsync(100UL, false, Arg.Any<CancellationToken>());

        using var scope = _notificationsServiceProvider.CreateScope();
        var notificationsDb = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

        var notifications = await notificationsDb.Notifications.ToListAsync();
        notifications.Should().ContainSingle(n => n.OrderId == orderResponse.Id);

        var processedMessages = await notificationsDb.ProcessedMessages.ToListAsync();
        processedMessages.Should().ContainSingle(p => p.EventId == envelope.EventId);
    }

    [Fact]
    public async Task CompleteTraceChain_WhenHeaderIsMissing_ShouldGenerateAndPropagateCorrelationIdDownstream()
    {
        // 1. Arrange: Client sends HTTP request WITHOUT X-Correlation-ID
        var command = new CreateOrderCommand("Mariana Costa", "mariana@example.com", 120.00m);

        // 2. Act: Orders API processes HTTP request
        var response = await _client.PostAsJsonAsync("/api/orders", command, _jsonOptions);

        // 3. Assert: Orders API generated a valid GUID Correlation ID and returned in response headers
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Contains(CorrelationConstants.HeaderName).Should().BeTrue();
        var generatedCorrelationId = response.Headers.GetValues(CorrelationConstants.HeaderName).First();
        generatedCorrelationId.Should().NotBeNullOrWhiteSpace();
        Guid.TryParse(generatedCorrelationId, out _).Should().BeTrue();

        var orderResponse = await response.Content.ReadFromJsonAsync<OrderResponse>(_jsonOptions);
        orderResponse.Should().NotBeNull();

        // Process Outbox to trigger publication
        await _factory.ProcessOutboxMessagesAsync();

        // 4. Assert: Event published contains the generated Correlation ID
        var publishedItem = _factory.EventPublisher.PublishedMessages
            .Last(m => m.Message is EventEnvelope<OrderCreatedIntegrationEvent> env && env.Data.OrderId == orderResponse!.Id);

        var envelope = (EventEnvelope<OrderCreatedIntegrationEvent>)publishedItem.Message;
        envelope.CorrelationId.Should().Be(generatedCorrelationId);

        // 5. Act: Notifications Service consumes the message
        var eventJson = JsonSerializer.Serialize(envelope);
        var messageBody = Encoding.UTF8.GetBytes(eventJson);

        var amqpPropsMock = Substitute.For<IReadOnlyBasicProperties>();
        amqpPropsMock.Type.Returns("OrderCreated");
        amqpPropsMock.CorrelationId.Returns(generatedCorrelationId);
        amqpPropsMock.MessageId.Returns(envelope.EventId.ToString());

        await _notificationsConsumer.ProcessMessageAsync(
            messageBody,
            amqpPropsMock,
            deliveryTag: 101UL,
            _rabbitMqChannelMock,
            CancellationToken.None);

        // 6. Assert: Notification persisted for that order
        await _rabbitMqChannelMock.Received(1).BasicAckAsync(101UL, false, Arg.Any<CancellationToken>());

        using var scope = _notificationsServiceProvider.CreateScope();
        var notificationsDb = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var notification = await notificationsDb.Notifications.FirstOrDefaultAsync(n => n.OrderId == orderResponse!.Id);
        notification.Should().NotBeNull();
    }

    public void Dispose()
    {
        _notificationsDbConnection.Dispose();
        _notificationsServiceProvider.Dispose();
    }
}
