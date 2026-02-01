using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MyDotNetApp.Models;
using System.Text;
using System.Text.Json;

namespace MyDotNetApp.Services;

/// <summary>
/// Kafka producer with Avro serialization and Client-Side Field Level Encryption (CSFLE)
/// Integrates with Azure Key Vault for encryption key management
/// </summary>
public interface IAvroKafkaProducerWithCSFLE
{
    Task<DeliveryResult<string, EncryptedAvroMessage>> ProduceAsync(
        string topic,
        string key,
        object payload,
        string eventType,
        Dictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default);
    
    void Flush(TimeSpan timeout);
}

public class AvroKafkaProducerWithCSFLE : IAvroKafkaProducerWithCSFLE, IDisposable
{
    private readonly ILogger<AvroKafkaProducerWithCSFLE> _logger;
    private readonly IAzureKeyVaultService _keyVaultService;
    private readonly IProducer<string, EncryptedAvroMessage> _producer;
    private readonly ISchemaRegistryClient _schemaRegistry;
    private readonly string _defaultTopic;

    public AvroKafkaProducerWithCSFLE(
        IConfiguration configuration,
        ILogger<AvroKafkaProducerWithCSFLE> logger,
        IAzureKeyVaultService keyVaultService)
    {
        _logger = logger;
        _keyVaultService = keyVaultService;

        var bootstrapServers = configuration["KafkaOutboxSettings:BootstrapServers"] ?? "localhost:9092";
        var schemaRegistryUrl = configuration["KafkaOutboxSettings:SchemaRegistryUrl"] ?? "http://localhost:8081";
        _defaultTopic = configuration["KafkaOutboxSettings:TopicName"] ?? "encrypted-avro-topic";

        // Configure Schema Registry client
        var schemaRegistryConfig = new SchemaRegistryConfig
        {
            Url = schemaRegistryUrl,
            RequestTimeoutMs = 5000,
            MaxCachedSchemas = 100
        };

        _schemaRegistry = new CachedSchemaRegistryClient(schemaRegistryConfig);

        // Configure Kafka producer
        var producerConfig = new ProducerConfig
        {
            BootstrapServers = bootstrapServers,
            Acks = Acks.Leader,
            CompressionType = CompressionType.Snappy,
            LingerMs = 10,
            BatchSize = 512 * 1024,
            QueueBufferingMaxMessages = 100000,
            QueueBufferingMaxKbytes = 262144,
            MessageTimeoutMs = 10000,
            RequestTimeoutMs = 10000,
            MaxInFlight = 5,
            EnableIdempotence = true,  // Ensure exactly-once semantics
            
            // Security settings (uncomment if using TLS/SASL)
            // SecurityProtocol = SecurityProtocol.SaslSsl,
            // SaslMechanism = SaslMechanism.Plain,
            // SaslUsername = configuration["Kafka:SaslUsername"],
            // SaslPassword = configuration["Kafka:SaslPassword"],
        };

        // Build producer with Avro serializer
        _producer = new ProducerBuilder<string, EncryptedAvroMessage>(producerConfig)
            .SetValueSerializer(new AvroSerializer<EncryptedAvroMessage>(_schemaRegistry, new AvroSerializerConfig
            {
                AutoRegisterSchemas = true,
                SubjectNameStrategy = SubjectNameStrategy.TopicRecord
            }))
            .SetErrorHandler((_, error) =>
            {
                _logger.LogError("Kafka producer error: Code={Code}, Reason={Reason}, IsFatal={IsFatal}",
                    error.Code, error.Reason, error.IsFatal);
            })
            .SetStatisticsHandler((_, json) =>
            {
                // Log producer statistics periodically
                _logger.LogDebug("Kafka producer stats: {Stats}", json);
            })
            .Build();

        _logger.LogInformation(
            "AvroKafkaProducerWithCSFLE initialized. Bootstrap={Bootstrap}, SchemaRegistry={SchemaRegistry}, Topic={Topic}",
            bootstrapServers, schemaRegistryUrl, _defaultTopic);
    }

    /// <summary>
    /// Produce an encrypted Avro message to Kafka
    /// </summary>
    /// <param name="topic">Kafka topic (optional, uses default if null)</param>
    /// <param name="key">Message key</param>
    /// <param name="payload">Payload object to encrypt</param>
    /// <param name="eventType">Event type identifier</param>
    /// <param name="metadata">Optional metadata</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task<DeliveryResult<string, EncryptedAvroMessage>> ProduceAsync(
        string topic,
        string key,
        object payload,
        string eventType,
        Dictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Serialize payload to JSON bytes
            var payloadJson = JsonSerializer.Serialize(payload);
            var payloadBytes = Encoding.UTF8.GetBytes(payloadJson);

            // Encrypt the payload using Azure Key Vault
            var (encryptedData, iv, keyId) = await _keyVaultService.EncryptDataAsync(payloadBytes, cancellationToken);

            // Create Avro message with encrypted payload
            var avroMessage = new EncryptedAvroMessage
            {
                Id = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                EventType = eventType,
                EncryptedPayload = encryptedData,
                KeyId = keyId,
                EncryptionAlgorithm = "AES256-CBC",
                IV = iv,
                Metadata = metadata != null ? JsonSerializer.Serialize(metadata) : null
            };

            // Produce to Kafka
            var targetTopic = string.IsNullOrEmpty(topic) ? _defaultTopic : topic;
            var result = await _producer.ProduceAsync(targetTopic, new Message<string, EncryptedAvroMessage>
            {
                Key = key,
                Value = avroMessage,
                Headers = new Headers
                {
                    { "encryption", Encoding.UTF8.GetBytes("CSFLE-AKV") },
                    { "event-type", Encoding.UTF8.GetBytes(eventType) },
                    { "key-id", Encoding.UTF8.GetBytes(keyId) }
                }
            }, cancellationToken);

            _logger.LogInformation(
                "Produced encrypted Avro message: Topic={Topic}, Partition={Partition}, Offset={Offset}, Key={Key}, EventType={EventType}",
                result.Topic, result.Partition.Value, result.Offset.Value, key, eventType);

            return result;
        }
        catch (ProduceException<string, EncryptedAvroMessage> ex)
        {
            _logger.LogError(ex,
                "Failed to produce message: Key={Key}, Error={Error}",
                key, ex.Error.Reason);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error producing message: Key={Key}", key);
            throw;
        }
    }

    /// <summary>
    /// Flush all pending messages
    /// </summary>
    public void Flush(TimeSpan timeout)
    {
        try
        {
            _producer.Flush(timeout);
            _logger.LogInformation("Kafka producer flushed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error flushing Kafka producer");
            throw;
        }
    }

    public void Dispose()
    {
        try
        {
            _producer?.Flush(TimeSpan.FromSeconds(30));
            _producer?.Dispose();
            _schemaRegistry?.Dispose();
            _logger.LogInformation("AvroKafkaProducerWithCSFLE disposed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing AvroKafkaProducerWithCSFLE");
        }
    }
}
