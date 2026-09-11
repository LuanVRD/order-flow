using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OrderFlow.Messaging.Contracts.Correlation;
using OrderFlow.Messaging.Contracts.Events;
using OrderFlow.Orders.Domain.Entities;
using OrderFlow.Orders.Domain.Events;

namespace OrderFlow.Orders.Infrastructure.Persistence.Interceptors;

public class OutboxSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly ICorrelationContextAccessor? _correlationContextAccessor;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public OutboxSaveChangesInterceptor(ICorrelationContextAccessor? correlationContextAccessor = null)
    {
        _correlationContextAccessor = correlationContextAccessor;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is null)
        {
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        ProcessDomainEvents(eventData.Context);

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        if (eventData.Context is null)
        {
            return base.SavingChanges(eventData, result);
        }

        ProcessDomainEvents(eventData.Context);

        return base.SavingChanges(eventData, result);
    }

    private void ProcessDomainEvents(Microsoft.EntityFrameworkCore.DbContext context)
    {
        var correlationId = _correlationContextAccessor?.CorrelationId;

        var ordersWithEvents = context.ChangeTracker
            .Entries<Order>()
            .Where(entry => entry.Entity.DomainEvents.Count > 0)
            .Select(entry => entry.Entity)
            .ToList();

        if (ordersWithEvents.Count == 0)
        {
            return;
        }

        var outboxMessages = new List<OutboxMessage>();

        foreach (var order in ordersWithEvents)
        {
            foreach (var domainEvent in order.DomainEvents)
            {
                var outboxMessage = MapToOutboxMessage(domainEvent, correlationId);
                if (outboxMessage != null)
                {
                    outboxMessages.Add(outboxMessage);
                }
            }

            order.ClearDomainEvents();
        }

        if (outboxMessages.Count > 0)
        {
            context.Set<OutboxMessage>().AddRange(outboxMessages);
        }
    }

    private static OutboxMessage? MapToOutboxMessage(IDomainEvent domainEvent, string? correlationId)
    {
        return domainEvent switch
        {
            OrderCreatedDomainEvent created => CreateOutboxMessage(
                "OrderCreated",
                EventEnvelope<OrderCreatedIntegrationEvent>.Create(
                    eventType: "OrderCreated",
                    data: new OrderCreatedIntegrationEvent(
                        created.OrderId,
                        created.CustomerName,
                        created.CustomerEmail,
                        created.TotalAmount,
                        created.Status.ToString(),
                        created.OccurredOn
                    ),
                    correlationId: correlationId,
                    occurredAt: created.OccurredOn
                ),
                created.OccurredOn
            ),

            OrderStatusChangedDomainEvent statusChanged => CreateOutboxMessage(
                "OrderStatusChanged",
                EventEnvelope<OrderStatusChangedIntegrationEvent>.Create(
                    eventType: "OrderStatusChanged",
                    data: new OrderStatusChangedIntegrationEvent(
                        statusChanged.OrderId,
                        statusChanged.PreviousStatus.ToString(),
                        statusChanged.NewStatus.ToString(),
                        statusChanged.OccurredOn
                    ),
                    correlationId: correlationId,
                    occurredAt: statusChanged.OccurredOn
                ),
                statusChanged.OccurredOn
            ),

            OrderCompletedDomainEvent completed => CreateOutboxMessage(
                "OrderCompleted",
                EventEnvelope<OrderCompletedIntegrationEvent>.Create(
                    eventType: "OrderCompleted",
                    data: new OrderCompletedIntegrationEvent(
                        completed.OrderId,
                        completed.OccurredOn
                    ),
                    correlationId: correlationId,
                    occurredAt: completed.OccurredOn
                ),
                completed.OccurredOn
            ),

            OrderCancelledDomainEvent cancelled => CreateOutboxMessage(
                "OrderCancelled",
                EventEnvelope<OrderCancelledIntegrationEvent>.Create(
                    eventType: "OrderCancelled",
                    data: new OrderCancelledIntegrationEvent(
                        cancelled.OrderId,
                        cancelled.PreviousStatus.ToString(),
                        cancelled.OccurredOn,
                        Reason: null
                    ),
                    correlationId: correlationId,
                    occurredAt: cancelled.OccurredOn
                ),
                cancelled.OccurredOn
            ),

            _ => null
        };
    }

    private static OutboxMessage CreateOutboxMessage<T>(string eventType, EventEnvelope<T> envelope, DateTimeOffset createdAt) where T : class
    {
        var payload = JsonSerializer.Serialize(envelope, SerializerOptions);
        return new OutboxMessage(
            id: envelope.EventId,
            type: eventType,
            version: envelope.Version,
            payload: payload,
            createdAt: createdAt
        );
    }
}
