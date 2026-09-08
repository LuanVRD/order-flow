namespace OrderFlow.Notifications.Infrastructure.Messaging;

public class RabbitMqOptions
{
    public const string SectionName = "RabbitMQ";

    public string HostName { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string VirtualHost { get; set; } = "/";
    public string ExchangeName { get; set; } = "orderflow.orders";
    public string ExchangeType { get; set; } = "topic";
    public string QueueName { get; set; } = "orderflow.notifications";
    public bool Durable { get; set; } = true;
    public bool AutoDelete { get; set; } = false;
    public ushort PrefetchCount { get; set; } = 10;
    public string DeadLetterExchangeName { get; set; } = "orderflow.notifications.dlx";
    public string DeadLetterExchangeType { get; set; } = "direct";
    public string DeadLetterQueueName { get; set; } = "orderflow.notifications.dlq";
    public string DeadLetterRoutingKey { get; set; } = "orderflow.notifications.dlq";
    public int MaxRetryAttempts { get; set; } = 3;
    public int InitialRetryDelayMs { get; set; } = 500;
    public string[] RoutingKeys { get; set; } =
    [
        "order.created",
        "order.status.changed",
        "order.completed",
        "order.cancelled"
    ];
}
