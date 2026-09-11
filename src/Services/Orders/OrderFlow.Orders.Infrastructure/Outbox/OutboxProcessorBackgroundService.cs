using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderFlow.Messaging.Contracts.Events;
using OrderFlow.Orders.Domain.Entities;
using OrderFlow.Orders.Infrastructure.Messaging;

namespace OrderFlow.Orders.Infrastructure.Outbox;

public class OutboxProcessorBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly OutboxOptions _options;
    private readonly ILogger<OutboxProcessorBackgroundService> _logger;
    private readonly string _instanceId;
    private DateTimeOffset _lastCleanupTime = DateTimeOffset.MinValue;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    public OutboxProcessorBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<OutboxOptions> options,
        ILogger<OutboxProcessorBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
        _instanceId = $"orders-outbox-{Guid.NewGuid():N}"[..24];
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OutboxProcessorBackgroundService started with InstanceId: {InstanceId}.", _instanceId);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_options.Enabled)
                {
                    var processedCount = await ProcessPendingBatchAsync(stoppingToken);

                    if (DateTimeOffset.UtcNow - _lastCleanupTime >= TimeSpan.FromMinutes(_options.CleanupIntervalMinutes))
                    {
                        await RunCleanupAsync(stoppingToken);
                    }

                    if (processedCount >= _options.BatchSize)
                    {
                        // There may be more messages waiting, yield briefly before next batch
                        await Task.Delay(100, stoppingToken);
                        continue;
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in OutboxProcessorBackgroundService execution loop.");
            }

            try
            {
                await Task.Delay(_options.PollingIntervalMs, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("OutboxProcessorBackgroundService stopping for InstanceId: {InstanceId}.", _instanceId);
    }

    public async Task<int> ProcessPendingBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
        var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();

        var lockDuration = TimeSpan.FromSeconds(_options.LockDurationSeconds);
        var messages = await repository.FetchAndLockPendingMessagesAsync(
            _options.BatchSize,
            _instanceId,
            lockDuration,
            cancellationToken);

        if (messages.Count == 0)
        {
            return 0;
        }

        _logger.LogDebug(
            "Outbox instance {InstanceId} acquired lock on {Count} pending outbox messages.",
            _instanceId,
            messages.Count);

        var successCount = 0;

        foreach (var message in messages)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var published = await TryPublishMessageAsync(message, publisher, repository, cancellationToken);
            if (published)
            {
                successCount++;
            }
        }

        return successCount;
    }

    private async Task<bool> TryPublishMessageAsync(
        OutboxMessage message,
        IEventPublisher publisher,
        IOutboxRepository repository,
        CancellationToken cancellationToken)
    {
        var routingKey = GetRoutingKey(message.Type);

        try
        {
            var envelope = DeserializeEnvelope(message);
            if (envelope == null)
            {
                throw new InvalidOperationException($"Failed to deserialize payload for outbox message {message.Id} of type '{message.Type}'.");
            }

            await publisher.PublishAsync(envelope, routingKey, cancellationToken);

            await repository.MarkAsProcessedAsync(message.Id, cancellationToken);

            _logger.LogInformation(
                "Outbox message {MessageId} of type '{EventType}' published successfully with routing key '{RoutingKey}'.",
                message.Id,
                message.Type,
                routingKey);

            return true;
        }
        catch (Exception ex)
        {
            var retryDelay = CalculateBackoffWithJitter(message.RetryCount);

            _logger.LogWarning(
                ex,
                "Failed to publish outbox message {MessageId} of type '{EventType}' (attempt {Attempt}). Next retry in {RetryDelayMs}ms.",
                message.Id,
                message.Type,
                message.RetryCount + 1,
                (int)retryDelay.TotalMilliseconds);

            try
            {
                await repository.RecordFailureAsync(message.Id, ex.Message, retryDelay, cancellationToken);
            }
            catch (Exception recordEx)
            {
                _logger.LogError(recordEx, "Failed to record failure for outbox message {MessageId}.", message.Id);
            }

            return false;
        }
    }

    private async Task RunCleanupAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();

            var cutoff = DateTimeOffset.UtcNow.AddDays(-_options.RetentionDays);
            var deletedCount = await repository.PurgeProcessedMessagesAsync(cutoff, cancellationToken);

            _lastCleanupTime = DateTimeOffset.UtcNow;

            if (deletedCount > 0)
            {
                _logger.LogInformation(
                    "Outbox cleanup purged {Count} processed messages older than {Cutoff} (retention: {RetentionDays} days).",
                    deletedCount,
                    cutoff,
                    _options.RetentionDays);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to execute outbox cleanup task.");
        }
    }

    public static string GetRoutingKey(string eventType) => eventType switch
    {
        "OrderCreated" => "order.created",
        "OrderStatusChanged" => "order.status.changed",
        "OrderCompleted" => "order.completed",
        "OrderCancelled" => "order.cancelled",
        _ => eventType.ToLowerInvariant()
    };

    private static object? DeserializeEnvelope(OutboxMessage message) => message.Type switch
    {
        "OrderCreated" => JsonSerializer.Deserialize<EventEnvelope<OrderCreatedIntegrationEvent>>(message.Payload, SerializerOptions),
        "OrderStatusChanged" => JsonSerializer.Deserialize<EventEnvelope<OrderStatusChangedIntegrationEvent>>(message.Payload, SerializerOptions),
        "OrderCompleted" => JsonSerializer.Deserialize<EventEnvelope<OrderCompletedIntegrationEvent>>(message.Payload, SerializerOptions),
        "OrderCancelled" => JsonSerializer.Deserialize<EventEnvelope<OrderCancelledIntegrationEvent>>(message.Payload, SerializerOptions),
        _ => JsonSerializer.Deserialize<JsonElement>(message.Payload, SerializerOptions)
    };

    private TimeSpan CalculateBackoffWithJitter(int retryCount)
    {
        var exponentialSeconds = Math.Min(_options.MaxDelaySeconds, _options.BaseDelaySeconds * Math.Pow(2, Math.Max(0, retryCount)));
        var jitterMs = Random.Shared.Next(0, 1000);
        return TimeSpan.FromSeconds(exponentialSeconds) + TimeSpan.FromMilliseconds(jitterMs);
    }
}
