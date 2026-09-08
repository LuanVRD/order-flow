using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OrderFlow.Notifications.Application;
using OrderFlow.Notifications.Application.Interfaces;
using OrderFlow.Notifications.Infrastructure;
using OrderFlow.Notifications.Infrastructure.Messaging;

namespace OrderFlow.Notifications.Tests.Infrastructure;

public class DependencyInjectionTests
{
    [Fact]
    public void AddInfrastructure_ShouldRegisterMessagingAndRepositoryServices()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:NotificationsConnection"] = "Data Source=test.db",
                ["RabbitMQ:HostName"] = "localhost",
                ["RabbitMQ:Port"] = "5672",
                ["RabbitMQ:ExchangeName"] = "orderflow.orders",
                ["RabbitMQ:QueueName"] = "orderflow.notifications"
            })
            .Build();

        services.AddLogging();
        services.AddNotificationsApplication();

        // Act
        services.AddInfrastructure(configuration);
        var provider = services.BuildServiceProvider();

        // Assert
        provider.GetService<INotificationRepository>().Should().NotBeNull();
        provider.GetService<IProcessedMessageRepository>().Should().NotBeNull();
        provider.GetService<IRabbitMqConnection>().Should().NotBeNull();
        provider.GetService<IOrderEventsConsumer>().Should().NotBeNull();
    }
}
