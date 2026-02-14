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
    
    bool IsHealthy();
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
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _keyVaultService = keyVaultService ?? throw new ArgumentNullException(nameof(keyVaultService));

        var bootstrapServers = configuration["KafkaOutboxSettings:BootstrapServers"] ?? "localhost:9092";
        var schemaRegistryUrl = configuration["KafkaOutboxSettings:SchemaRegistryUrl"] ?? "http://localhost:8081";
        _defaultTopic = configuration["KafkaOutboxSettings:TopicName"] ?? "encrypted-avro-topic";

        _logger.LogDebug("Initializing AvroKafkaProducerWithCSFLE with Bootstrap={Bootstrap}, SchemaRegistry={SchemaRegistry}", 
            bootstrapServers, schemaRegistryUrl);

        try
        {
            // Configure Schema Registry client
            var schemaRegistryConfig = new SchemaRegistryConfig
            {
                Url = schemaRegistryUrl,
                RequestTimeoutMs = 5000,
                MaxCachedSchemas = 100
            };

            _schemaRegistry = new CachedSchemaRegistryClient(schemaRegistryConfig);
            _logger.LogDebug("✅ Schema Registry client created: {Url}", schemaRegistryUrl);

            // Configure Kafka producer
            var producerConfig = new ProducerConfig
            {
                BootstrapServers = bootstrapServers,
                ClientId = "avro-encrypted-producer",
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
                
                // ✅ ENABLE DEBUG OUTPUT
                Debug = "broker,topic,metadata,protocol,serializer",
                LogConnectionClose = true,
                
                // Security settings (uncomment if using TLS/SASL)
                // SecurityProtocol = SecurityProtocol.SaslSsl,
                // SaslMechanism = SaslMechanism.Plain,
                // SaslUsername = configuration["Kafka:SaslUsername"],
                // SaslPassword = configuration["Kafka:SaslPassword"],
            };

            // Build producer with Avro serializer (both key and value)
            _producer = new ProducerBuilder<string, EncryptedAvroMessage>(producerConfig)
                .SetKeySerializer(Serializers.Utf8)  // String key serializer
                .SetValueSerializer(new AvroSerializer<EncryptedAvroMessage>(_schemaRegistry, new AvroSerializerConfig
                {
                    AutoRegisterSchemas = true,
                    SubjectNameStrategy = SubjectNameStrategy.TopicRecord
                }))
                .SetLogHandler((producer, logMessage) =>
                {
                    // ✅ CAPTURE CONFLUENT LOGS
                    var level = logMessage.Level switch
                    {
                        SyslogLevel.Emerg => LogLevel.Critical,
                        SyslogLevel.Alert => LogLevel.Critical,
                        SyslogLevel.Crit => LogLevel.Critical,
                        SyslogLevel.Err => LogLevel.Error,
                        SyslogLevel.Warning => LogLevel.Warning,
                        SyslogLevel.Notice => LogLevel.Information,
                        SyslogLevel.Info => LogLevel.Debug,
                        SyslogLevel.Debug => LogLevel.Trace,
                        _ => LogLevel.Information
                    };

                    _logger.Log(level, 
                        "🔧 Confluent.Kafka [{Facility}] {Message}", 
                        logMessage.Facility, 
                        logMessage.Message);
                })
                .SetErrorHandler((_, error) =>
                {
                    _logger.LogError("❌ Kafka producer error: Code={Code}, Reason={Reason}, IsFatal={IsFatal}",
                        error.Code, error.Reason, error.IsFatal);
                })
                .SetStatisticsHandler((_, json) =>
                {
                    // Log producer statistics periodically
                    _logger.LogDebug("📊 Kafka producer stats: {Stats}", json);
                })
                .SetDeliveryReportHandler((deliveryReport) =>
                {
                    if (deliveryReport.Error.Code != ErrorCode.NoError)
                    {
                        _logger.LogError("❌ Delivery report error: {Error}", deliveryReport.Error.Reason);
                    }
                    else
                    {
                        _logger.LogDebug("✅ Message delivered: Topic={Topic}, Partition={Partition}, Offset={Offset}",
                            deliveryReport.Topic, deliveryReport.Partition, deliveryReport.Offset);
                    }
                })
                .Build();

            _logger.LogInformation(
                "✅ AvroKafkaProducerWithCSFLE initialized. Bootstrap={Bootstrap}, SchemaRegistry={SchemaRegistry}, DefaultTopic={Topic}",
                bootstrapServers, schemaRegistryUrl, _defaultTopic);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to initialize AvroKafkaProducerWithCSFLE");
            throw;
        }
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
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be null or empty", nameof(key));
        if (payload == null)
            throw new ArgumentNullException(nameof(payload));
        if (_producer == null)
            throw new InvalidOperationException("Producer has not been initialized");

        try
        {
            _logger.LogDebug("🔐 ENCRYPTION PIPELINE START: Key={Key}, EventType={EventType}", key, eventType);

            // Serialize payload to JSON bytes
            var payloadJson = JsonSerializer.Serialize(payload);
            var payloadBytes = Encoding.UTF8.GetBytes(payloadJson);
            _logger.LogDebug("  [1/5] Payload serialized: {PayloadSize} bytes", payloadBytes.Length);

            // Encrypt the payload using Azure Key Vault
            _logger.LogDebug("  [2/5] Calling Azure Key Vault for encryption...");
            var encryptStartTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var (encryptedData, iv, keyId) = await _keyVaultService.EncryptDataAsync(payloadBytes, cancellationToken);
            
            if (encryptedData == null || encryptedData.Length == 0)
                throw new InvalidOperationException("Encryption returned null or empty data");
            
            var encryptDuration = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - encryptStartTime;
            
            _logger.LogInformation(
                "✅ ENCRYPTED: OriginalSize={OriginalSize}→{EncryptedSize} bytes, Compression={Compression}%, " +
                "Algorithm=AES256-CBC, KeyId={KeyId}, Duration={Duration}ms",
                payloadBytes.Length, encryptedData.Length,
                (int)((1 - (decimal)encryptedData.Length / payloadBytes.Length) * 100),
                keyId, encryptDuration);

            // Create Avro message with encrypted payload
            _logger.LogDebug("  [3/5] Creating Avro message with encrypted payload");
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
            _logger.LogDebug("  [4/5] Serializing to Kafka with Schema Registry...");
            var targetTopic = string.IsNullOrEmpty(topic) ? _defaultTopic : topic;
            var produceStartTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            
            DeliveryResult<string, EncryptedAvroMessage> result = null!;
            try
            {
                result = await _producer.ProduceAsync(targetTopic, new Message<string, EncryptedAvroMessage>
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
            }
            catch (OperationCanceledException)
            {
                _logger.LogError("❌ ProduceAsync was cancelled for Key={Key}", key);
                throw;
            }

            if (result == null)
            {
                _logger.LogError("❌ NULL RESULT: ProduceAsync returned null for Key={Key}, Topic={Topic}", key, targetTopic);
                throw new InvalidOperationException($"ProduceAsync returned null result for key {key}");
            }

            var produceDuration = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - produceStartTime;

            _logger.LogInformation(
                "✅ [5/5] PRODUCED to Kafka: Topic={Topic}, Partition={Partition}, Offset={Offset}, " +
                "Key={Key}, MessageSize={MessageSize}, Headers=[encryption:CSFLE-AKV,key-id:{KeyId}], Duration={Duration}ms",
                result.Topic, result.Partition.Value, result.Offset.Value, key, 
                avroMessage.EncryptedPayload.Length, keyId, produceDuration);

            _logger.LogDebug("🔓 ENCRYPTION PIPELINE COMPLETE (Total: {TotalDuration}ms)", encryptDuration + produceDuration);

            return result;
        }
        catch (ProduceException<string, EncryptedAvroMessage> ex)
        {
            _logger.LogError(ex,
                "❌ FAILED TO PRODUCE: Key={Key}, Error={Error}, IsFatal={IsFatal}",
                key, ex.Error.Reason, ex.Error.IsFatal);
            throw;
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogError(ex, "❌ OPERATION CANCELLED in encryption pipeline: Key={Key}", key);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ UNEXPECTED ERROR in encryption pipeline: Key={Key}, Type={ExceptionType}", 
                key, ex.GetType().Name);
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
            if (_producer == null)
            {
                _logger.LogWarning("⚠️ Cannot flush: Producer is null");
                return;
            }

            _logger.LogDebug("Flushing producer with timeout {Timeout}ms...", timeout.TotalMilliseconds);
            _producer.Flush(timeout);
            _logger.LogInformation("✅ Kafka producer flushed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error flushing Kafka producer");
            throw;
        }
    }

    /// <summary>
    /// Health check for the producer
    /// </summary>
    public bool IsHealthy()
    {
        try
        {
            if (_producer == null)
            {
                _logger.LogWarning("⚠️ Health check failed: Producer is null");
                return false;
            }

            // Simple health check: ensure producer can be used
            _logger.LogDebug("Health check: Producer is initialized");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Health check failed");
            return false;
        }
    }

    public void Dispose()
    {
        try
        {
            if (_producer == null)
            {
                _logger.LogDebug("Producer already null, skipping disposal");
                return;
            }

            _logger.LogDebug("Flushing producer before disposal...");
            try
            {
                _producer.Flush(TimeSpan.FromSeconds(30));
                _logger.LogDebug("✅ Producer flushed before disposal");
            }
            catch (Exception flushEx)
            {
                _logger.LogWarning(flushEx, "⚠️ Error flushing producer during disposal");
            }

            _producer.Dispose();
            _logger.LogDebug("✅ Producer disposed");

            _schemaRegistry?.Dispose();
            _logger.LogDebug("✅ Schema Registry disposed");

            _logger.LogInformation("✅ AvroKafkaProducerWithCSFLE disposed completely");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error disposing AvroKafkaProducerWithCSFLE");
        }
    }
}
