namespace OrderFlow.Messaging.Contracts.Correlation;

public class CorrelationContextAccessor : ICorrelationContextAccessor
{
    private static readonly AsyncLocal<string?> _asyncLocalCorrelationId = new();
    private string? _scopedCorrelationId;

    public string? CorrelationId
    {
        get => _scopedCorrelationId ?? _asyncLocalCorrelationId.Value;
        set
        {
            _scopedCorrelationId = value;
            _asyncLocalCorrelationId.Value = value;
        }
    }
}
