namespace OrderFlow.Messaging.Contracts.Correlation;

public interface ICorrelationContextAccessor
{
    string? CorrelationId { get; set; }
}
