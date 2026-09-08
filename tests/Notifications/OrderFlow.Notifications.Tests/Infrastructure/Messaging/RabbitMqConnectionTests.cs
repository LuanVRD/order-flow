using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using OrderFlow.Notifications.Infrastructure.Messaging;
using RabbitMQ.Client;

namespace OrderFlow.Notifications.Tests.Infrastructure.Messaging;

public class RabbitMqConnectionTests
{
    private readonly IOptions<RabbitMqOptions> _options;
    private readonly ILogger<RabbitMqConnection> _loggerMock;

    public RabbitMqConnectionTests()
    {
        var options = new RabbitMqOptions
        {
            HostName = "localhost",
            Port = 5672,
            UserName = "guest",
            Password = "guest",
            VirtualHost = "/",
            ExchangeName = "orderflow.orders",
            ExchangeType = "topic",
            QueueName = "orderflow.notifications",
            Durable = true,
            AutoDelete = false,
            RoutingKeys =
            [
                "order.created",
                "order.status.changed",
                "order.completed",
                "order.cancelled"
            ]
        };

        _options = Options.Create(options);
        _loggerMock = Substitute.For<ILogger<RabbitMqConnection>>();
    }

    [Fact]
    public void RabbitMqOptions_ShouldHaveDefaultValues()
    {
        // Arrange & Act
        var options = new RabbitMqOptions();

        // Assert
        options.HostName.Should().Be("localhost");
        options.Port.Should().Be(5672);
        options.UserName.Should().Be("guest");
        options.Password.Should().Be("guest");
        options.VirtualHost.Should().Be("/");
        options.ExchangeName.Should().Be("orderflow.orders");
        options.ExchangeType.Should().Be("topic");
        options.QueueName.Should().Be("orderflow.notifications");
        options.Durable.Should().BeTrue();
        options.AutoDelete.Should().BeFalse();
        options.PrefetchCount.Should().Be(10);
        options.RoutingKeys.Should().Contain(
            "order.created",
            "order.status.changed",
            "order.completed",
            "order.cancelled"
        );
    }
}
