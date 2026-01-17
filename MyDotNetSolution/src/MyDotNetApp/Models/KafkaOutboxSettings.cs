namespace MyDotNetApp.Models;

public class KafkaOutboxSettings
{
    public string? BootstrapServers { get; set; }
    public string? TopicName { get; set; }
    public string? SchemaRegistryUrl { get; set; }
    public int BatchSize { get; set; } = 50000;
    public int PollingIntervalMs { get; set; } = 10;
    public int MaxConcurrentProducers { get; set; } = 20;
    public int MaxProducerBuffer { get; set; } = 200000;
    public int DatabaseConnectionPoolSize { get; set; } = 40;

    // Adaptive backoff settings - reduces polling when idle
    public bool EnableAdaptiveBackoff { get; set; } = true;
    public int MaxPollingIntervalMs { get; set; } = 5000;  // Max 5s delay when idle
    public int IdleThresholdMs { get; set; } = 100;  // Consider idle if empty for 100ms
    public double BackoffMultiplier { get; set; } = 1.5;  // Increase delay by 1.5x each idle cycle
}

