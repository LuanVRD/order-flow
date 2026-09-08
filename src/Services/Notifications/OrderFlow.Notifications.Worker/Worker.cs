using OrderFlow.Notifications.Infrastructure.Messaging;

namespace OrderFlow.Notifications.Worker;

public class Worker : BackgroundService
{
    private readonly IOrderEventsConsumer _consumer;
    private readonly ILogger<Worker> _logger;

    public Worker(IOrderEventsConsumer consumer, ILogger<Worker> logger)
    {
        _consumer = consumer;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OrderFlow Notifications Worker starting...");

        try
        {
            await _consumer.StartConsumingAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("OrderFlow Notifications Worker received cancellation signal.");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "OrderFlow Notifications Worker terminated unexpectedly due to an unhandled exception.");
            throw;
        }
        finally
        {
            _logger.LogInformation("OrderFlow Notifications Worker has stopped.");
        }
    }
}
