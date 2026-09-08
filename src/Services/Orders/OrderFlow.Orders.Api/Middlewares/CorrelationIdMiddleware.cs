using OrderFlow.Messaging.Contracts.Correlation;

namespace OrderFlow.Orders.Api.Middlewares;

public class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, ICorrelationContextAccessor correlationContextAccessor)
    {
        string correlationId;

        if (context.Request.Headers.TryGetValue(CorrelationConstants.HeaderName, out var headerValue) &&
            !string.IsNullOrWhiteSpace(headerValue))
        {
            correlationId = headerValue.ToString().Trim();
        }
        else
        {
            correlationId = Guid.NewGuid().ToString();
        }

        correlationContextAccessor.CorrelationId = correlationId;

        context.Response.OnStarting(() =>
        {
            if (!context.Response.Headers.ContainsKey(CorrelationConstants.HeaderName))
            {
                context.Response.Headers[CorrelationConstants.HeaderName] = correlationId;
            }
            return Task.CompletedTask;
        });

        // Also proactively set on response headers if not already started
        if (!context.Response.HasStarted && !context.Response.Headers.ContainsKey(CorrelationConstants.HeaderName))
        {
            context.Response.Headers[CorrelationConstants.HeaderName] = correlationId;
        }

        using (_logger.BeginScope(new Dictionary<string, object>
        {
            [CorrelationConstants.PropertyName] = correlationId
        }))
        {
            await _next(context);
        }
    }
}
