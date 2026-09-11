namespace OrderFlow.Orders.Infrastructure.Outbox;

public class OutboxOptions
{
    public const string SectionName = "Outbox";

    public bool Enabled { get; set; } = true;
    public int BatchSize { get; set; } = 50;
    public int PollingIntervalMs { get; set; } = 2000;
    public int LockDurationSeconds { get; set; } = 30;
    public int MaxRetryAttempts { get; set; } = 5;
    public double BaseDelaySeconds { get; set; } = 1.0;
    public double MaxDelaySeconds { get; set; } = 60.0;
    public int RetentionDays { get; set; } = 7;
    public int CleanupIntervalMinutes { get; set; } = 60;
}
