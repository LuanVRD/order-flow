using OrderFlow.Orders.Domain.Enums;
using OrderFlow.Orders.Domain.Events;
using OrderFlow.Orders.Domain.Exceptions;

namespace OrderFlow.Orders.Domain.Entities;

public class Order
{
    private readonly List<IDomainEvent> _domainEvents = new();

    public Guid Id { get; private set; }
    public string CustomerName { get; private set; } = string.Empty;
    public string CustomerEmail { get; private set; } = string.Empty;
    public OrderStatus Status { get; private set; }
    public decimal TotalAmount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    // Private constructor for EF Core or deserialization frameworks
    private Order()
    {
    }

    public Order(string customerName, string customerEmail, decimal totalAmount)
    {
        if (string.IsNullOrWhiteSpace(customerName))
        {
            throw new DomainException("Customer name is required and cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(customerEmail) || !customerEmail.Contains('@') || customerEmail.StartsWith('@') || customerEmail.EndsWith('@'))
        {
            throw new DomainException("A valid customer email address is required.");
        }

        if (totalAmount <= 0)
        {
            throw new DomainException("Total amount must be greater than zero.");
        }

        Id = Guid.NewGuid();
        CustomerName = customerName.Trim();
        CustomerEmail = customerEmail.Trim();
        TotalAmount = totalAmount;
        Status = OrderStatus.Pending;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = null;

        AddDomainEvent(new OrderCreatedDomainEvent(Id, CustomerName, CustomerEmail, TotalAmount, Status, CreatedAt));
    }

    public void ChangeStatus(OrderStatus newStatus)
    {
        if (Status == newStatus)
        {
            throw new DomainException($"Order is already in '{Status}' status.");
        }

        bool isValidTransition = (Status, newStatus) switch
        {
            (OrderStatus.Pending, OrderStatus.Processing) => true,
            (OrderStatus.Processing, OrderStatus.Completed) => true,
            (OrderStatus.Pending, OrderStatus.Cancelled) => true,
            (OrderStatus.Processing, OrderStatus.Cancelled) => true,
            _ => false
        };

        if (!isValidTransition)
        {
            throw new DomainException($"Cannot transition order status from '{Status}' to '{newStatus}'.");
        }

        var previousStatus = Status;
        Status = newStatus;
        UpdatedAt = DateTimeOffset.UtcNow;

        AddDomainEvent(new OrderStatusChangedDomainEvent(Id, previousStatus, newStatus, UpdatedAt.Value));

        if (newStatus == OrderStatus.Completed)
        {
            AddDomainEvent(new OrderCompletedDomainEvent(Id, UpdatedAt.Value));
        }
        else if (newStatus == OrderStatus.Cancelled)
        {
            AddDomainEvent(new OrderCancelledDomainEvent(Id, previousStatus, UpdatedAt.Value));
        }
    }

    public void StartProcessing() => ChangeStatus(OrderStatus.Processing);

    public void Complete() => ChangeStatus(OrderStatus.Completed);

    public void Cancel() => ChangeStatus(OrderStatus.Cancelled);

    protected void AddDomainEvent(IDomainEvent domainEvent)
    {
        _domainEvents.Add(domainEvent);
    }

    public void ClearDomainEvents()
    {
        _domainEvents.Clear();
    }
}

