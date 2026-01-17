namespace MyDotNetApp.Models;

public class KafkaOutboxSettings
{
    public string? BootstrapServers { get; set; }
    public string? TopicName { get; set; }
    public string? SchemaRegistryUrl { get; set; }
    public int BatchSize { get; set; } = 50000;  // Increased from 10k for higher throughput
    public int PollingIntervalMs { get; set; } = 10;  // Reduced from 100ms for faster polling
    public int MaxConcurrentProducers { get; set; } = 20;  // Increased from 10 for more parallelism
    public int MaxProducerBuffer { get; set; } = 200000;  // Increased from 50k to handle burst traffic
    public int DatabaseConnectionPoolSize { get; set; } = 40;  // Increased from 20 for more db concurrency
}
