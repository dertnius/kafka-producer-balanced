namespace MyDotNetApp.Models;

public class KafkaOutboxSettings
{
    public string? BootstrapServers { get; set; }
    public string? TopicName { get; set; }
    public string? SchemaRegistryUrl { get; set; }
    public int BatchSize { get; set; } = 50000;
    public int PollingIntervalMs { get; set; } = 100;  // How often to poll for new messages (ms)
    public int MaxConcurrentProducers { get; set; } = 20;
    public int MaxProducerBuffer { get; set; } = 200000;
    public int DatabaseConnectionPoolSize { get; set; } = 40;
}

