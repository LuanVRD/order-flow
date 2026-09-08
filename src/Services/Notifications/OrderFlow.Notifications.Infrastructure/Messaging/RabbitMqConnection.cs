using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace OrderFlow.Notifications.Infrastructure.Messaging;

public class RabbitMqConnection : IRabbitMqConnection
{
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqConnection> _logger;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly SemaphoreSlim _topologyLock = new(1, 1);
    private IConnection? _connection;
    private bool _topologyInitialized;
    private bool _disposed;

    public RabbitMqConnection(IOptions<RabbitMqOptions> options, ILogger<RabbitMqConnection> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public bool IsConnected => _connection is { IsOpen: true } && !_disposed;

    private async Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (IsConnected && _connection != null)
        {
            return _connection;
        }

        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            if (IsConnected && _connection != null)
            {
                return _connection;
            }

            var factory = new ConnectionFactory
            {
                HostName = _options.HostName,
                Port = _options.Port,
                UserName = _options.UserName,
                Password = _options.Password,
                VirtualHost = _options.VirtualHost
            };

            _logger.LogInformation(
                "Establishing RabbitMQ connection to {HostName}:{Port} (vHost: {VirtualHost})...",
                _options.HostName,
                _options.Port,
                _options.VirtualHost);

            _connection = await factory.CreateConnectionAsync(cancellationToken);

            _logger.LogInformation("RabbitMQ connection established successfully.");
            return _connection;
        }
        catch (BrokerUnreachableException ex)
        {
            _logger.LogError(ex, "Failed to connect to RabbitMQ broker at {HostName}:{Port}.", _options.HostName, _options.Port);
            throw;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public async Task<IChannel> CreateChannelAsync(CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken);
        return await connection.CreateChannelAsync(cancellationToken: cancellationToken);
    }

    public async Task InitializeTopologyAsync(CancellationToken cancellationToken = default)
    {
        if (_topologyInitialized)
        {
            return;
        }

        await _topologyLock.WaitAsync(cancellationToken);
        try
        {
            if (_topologyInitialized)
            {
                return;
            }

            using var channel = await CreateChannelAsync(cancellationToken);

            // 1. Declare Dead Letter Exchange & Queue
            _logger.LogInformation(
                "Declaring Dead Letter Exchange '{DlExchangeName}' (Type: {DlExchangeType}, Durable: {Durable})...",
                _options.DeadLetterExchangeName,
                _options.DeadLetterExchangeType,
                _options.Durable);

            await channel.ExchangeDeclareAsync(
                exchange: _options.DeadLetterExchangeName,
                type: _options.DeadLetterExchangeType,
                durable: _options.Durable,
                autoDelete: _options.AutoDelete,
                arguments: null,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Declaring Dead Letter Queue '{DlQueueName}' (Durable: {Durable}, AutoDelete: {AutoDelete})...",
                _options.DeadLetterQueueName,
                _options.Durable,
                _options.AutoDelete);

            await channel.QueueDeclareAsync(
                queue: _options.DeadLetterQueueName,
                durable: _options.Durable,
                exclusive: false,
                autoDelete: _options.AutoDelete,
                arguments: null,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Binding Dead Letter Queue '{DlQueueName}' to exchange '{DlExchangeName}' with routing key '{DlRoutingKey}'...",
                _options.DeadLetterQueueName,
                _options.DeadLetterExchangeName,
                _options.DeadLetterRoutingKey);

            await channel.QueueBindAsync(
                queue: _options.DeadLetterQueueName,
                exchange: _options.DeadLetterExchangeName,
                routingKey: _options.DeadLetterRoutingKey,
                arguments: null,
                cancellationToken: cancellationToken);

            // 2. Declare Business Exchange & Main Queue with Dead-Lettering
            _logger.LogInformation(
                "Declaring exchange '{ExchangeName}' (Type: {ExchangeType}, Durable: {Durable})...",
                _options.ExchangeName,
                _options.ExchangeType,
                _options.Durable);

            await channel.ExchangeDeclareAsync(
                exchange: _options.ExchangeName,
                type: _options.ExchangeType,
                durable: _options.Durable,
                autoDelete: _options.AutoDelete,
                arguments: null,
                cancellationToken: cancellationToken);

            var mainQueueArgs = new Dictionary<string, object?>
            {
                { "x-dead-letter-exchange", _options.DeadLetterExchangeName },
                { "x-dead-letter-routing-key", _options.DeadLetterRoutingKey }
            };

            _logger.LogInformation(
                "Declaring queue '{QueueName}' with DLX '{DlExchangeName}' (Durable: {Durable}, AutoDelete: {AutoDelete})...",
                _options.QueueName,
                _options.DeadLetterExchangeName,
                _options.Durable,
                _options.AutoDelete);

            await channel.QueueDeclareAsync(
                queue: _options.QueueName,
                durable: _options.Durable,
                exclusive: false,
                autoDelete: _options.AutoDelete,
                arguments: mainQueueArgs,
                cancellationToken: cancellationToken);

            foreach (var routingKey in _options.RoutingKeys)
            {
                _logger.LogInformation(
                    "Binding queue '{QueueName}' to exchange '{ExchangeName}' with routing key '{RoutingKey}'...",
                    _options.QueueName,
                    _options.ExchangeName,
                    routingKey);

                await channel.QueueBindAsync(
                    queue: _options.QueueName,
                    exchange: _options.ExchangeName,
                    routingKey: routingKey,
                    arguments: null,
                    cancellationToken: cancellationToken);
            }

            _topologyInitialized = true;
            _logger.LogInformation(
                "RabbitMQ topology initialized successfully for queue '{QueueName}', DLQ '{DlQueueName}' and exchange '{ExchangeName}'.",
                _options.QueueName,
                _options.DeadLetterQueueName,
                _options.ExchangeName);
        }
        finally
        {
            _topologyLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_connection != null)
        {
            try
            {
                await _connection.CloseAsync();
                await _connection.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error while closing RabbitMQ connection during disposal.");
            }
        }

        _connectionLock.Dispose();
        _topologyLock.Dispose();
        GC.SuppressFinalize(this);
    }
}
