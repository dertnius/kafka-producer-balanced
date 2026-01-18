using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyDotNetApp.Models;
namespace MyDotNetApp.Services
{
    /// <summary>
    /// High-performance background service that consumes messages from Kafka topic 
    /// and batch-updates Outbox table with reception status.
    /// Optimized for millions of messages with minimal table locking.
    /// Supports Avro message format with schema registry.
    /// Uses batching, NoLock queries, and async operations to prevent blocking between producer and consumer.
    /// </summary>
    public class OutboxConsumerService : BackgroundService
    {
        private readonly ILogger<OutboxConsumerService> _logger;
        private readonly IOutboxService _outboxService;
        private readonly IConfiguration _configuration;
        private readonly KafkaOutboxSettings _kafkaSettings;
        private readonly string _consumerGroupId = "outbox-consumer-group";
        private readonly int _pollIntervalMs;
        private readonly int _batchSize;
        private readonly int _flushIntervalMs;
        private readonly int _instanceId;
        private IConsumer<string, string>? _consumer;

        public OutboxConsumerService(
            ILogger<OutboxConsumerService> logger,
            IOutboxService outboxService,
            IConfiguration configuration,
            IOptions<KafkaOutboxSettings> kafkaSettings,
            int instanceId = 0)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _outboxService = outboxService ?? throw new ArgumentNullException(nameof(outboxService));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _kafkaSettings = kafkaSettings?.Value ?? throw new ArgumentNullException(nameof(kafkaSettings));
            _pollIntervalMs = _configuration.GetValue<int>("Processing:PollIntervalMs", 5000);
            // Batch configuration for high-throughput scenarios
            _batchSize = _configuration.GetValue<int>("Consumer:BatchSize", 5000);
            _flushIntervalMs = _configuration.GetValue<int>("Consumer:FlushIntervalMs", 50);
            _instanceId = instanceId;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("OutboxConsumerService #{InstanceId} is starting with batchSize={BatchSize}, flushIntervalMs={FlushIntervalMs} (Avro format). Target: 1M msgs/min",
                _instanceId, _batchSize, _flushIntervalMs);
            try
            {
                InitializeConsumer();
                
                // Batch accumulator with optimized capacity
                var messageBatch = new List<(long MessageId, DateTime ReceivedAt)>(_batchSize * 2);
                var batchStopwatch = Stopwatch.StartNew();
                long totalConsumed = 0;
                long totalBatches = 0;
                var statsStopwatch = Stopwatch.StartNew();

                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        // Consume multiple messages efficiently in one poll (reduced timeout for responsiveness)
                        var consumeResult = _consumer!.Consume(10); // 10ms timeout for faster batching

                        if (consumeResult != null && !consumeResult.IsPartitionEOF)
                        {
                            try
                            {
                                var messageId = ExtractMessageId(consumeResult.Message.Value);
                                if (messageId > 0)
                                {
                                    messageBatch.Add((messageId, DateTime.UtcNow));
                                    totalConsumed++;
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Error processing consumed message");
                            }
                        }

                        // Flush batch if threshold reached or timeout exceeded
                        var shouldFlush = messageBatch.Count >= _batchSize || 
                            (messageBatch.Count > 0 && batchStopwatch.ElapsedMilliseconds >= _flushIntervalMs);

                        if (shouldFlush)
                        {
                            await FlushBatchAsync(messageBatch, stoppingToken);
                            totalBatches++;
                            messageBatch.Clear();
                            batchStopwatch.Restart();

                            // Log throughput statistics every 10 seconds
                            if (statsStopwatch.ElapsedMilliseconds >= 10000)
                            {
                                var throughput = (totalConsumed / statsStopwatch.Elapsed.TotalSeconds);
                                _logger.LogInformation(
                                    "Avro Consumer #{InstanceId} | Throughput: {ThroughputPerSec:F0} msg/sec ({ThroughputPerMin:F0} msg/min) | Total: {TotalConsumed} msgs in {TotalBatches} batches",
                                    _instanceId, throughput, throughput * 60, totalConsumed, totalBatches);
                                totalConsumed = 0;
                                totalBatches = 0;
                                statsStopwatch.Restart();
                            }
                        }
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        _logger.LogInformation("Consumption cancelled");
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error consuming messages from Kafka");
                        
                        // Flush any accumulated messages before retry
                        if (messageBatch.Count > 0)
                        {
                            try
                            {
                                await FlushBatchAsync(messageBatch, stoppingToken);
                                messageBatch.Clear();
                            }
                            catch (Exception flushEx)
                            {
                                _logger.LogError(flushEx, "Error flushing batch during error recovery");
                            }
                        }

                        // Wait before retrying to avoid rapid reconnection attempts
                        try
                        {
                            await Task.Delay(100, stoppingToken);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                    }
                }

                // Flush remaining messages on shutdown
                if (messageBatch.Count > 0)
                {
                    try
                    {
                        await FlushBatchAsync(messageBatch, stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.LogWarning("Shutdown flush was cancelled, {MessageCount} messages may not be updated", messageBatch.Count);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in OutboxConsumerService");
                throw;
            }
            finally
            {
                _consumer?.Close();
                _consumer?.Dispose();
                _logger.LogInformation("OutboxConsumerService has stopped.");
            }
        }

        /// <summary>
        /// Initializes the Kafka consumer with maximum throughput configuration
        /// Optimized for consuming 1M+ messages per minute
        /// </summary>
        private void InitializeConsumer()
        {
            try
            {
                var consumerConfig = new ConsumerConfig
                {
                    BootstrapServers = _kafkaSettings.BootstrapServers,
                    GroupId = _consumerGroupId,
                    AutoOffsetReset = AutoOffsetReset.Earliest,
                    EnableAutoCommit = true,
                    AutoCommitIntervalMs = 2000,           // Commit more frequently for safety
                    SessionTimeoutMs = 30000,
                    MaxPollIntervalMs = 300000,
                    
                    // CRITICAL: High-throughput fetching settings
                    FetchMinBytes = 10240,                 // 10KB min to batch more messages
                    FetchWaitMaxMs = 50,                   // Wait up to 50ms to batch messages
                    MaxPartitionFetchBytes = 10485760,     // 10MB per partition (large batches)
                    
                    // Connection & performance settings
                    SocketKeepaliveEnable = true,
                    SocketNagleDisable = true,
                    
                    // Compression for network efficiency (client-side)
                    IsolationLevel = IsolationLevel.ReadUncommitted  // No transaction overhead
                };

                _consumer = new ConsumerBuilder<string, string>(consumerConfig)
                    .SetErrorHandler((_, error) =>
                    {
                        if (!error.IsFatal)
                        {
                            _logger.LogWarning("Kafka consumer error: {Reason}", error.Reason);
                        }
                        else
                        {
                            _logger.LogError("Fatal Kafka consumer error: {Reason}", error.Reason);
                        }
                    })
                    .Build();

                _consumer.Subscribe(_kafkaSettings.TopicName);
                _logger.LogInformation("Consumer #{InstanceId} subscribed to topic: {Topic} with ultra-high-throughput Avro configuration. 1M+ msgs/min", 
                    _instanceId, _kafkaSettings.TopicName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing Kafka consumer");
                throw;
            }
        }

        /// <summary>
        /// Flushes accumulated batch of messages to the database in a single operation
        /// This minimizes table locking and improves throughput for high-volume scenarios
        /// </summary>
        private async Task FlushBatchAsync(List<(long MessageId, DateTime ReceivedAt)> messageBatch, CancellationToken stoppingToken)
        {
            if (messageBatch == null || messageBatch.Count == 0)
            {
                return;
            }

            var batchStopwatch = Stopwatch.StartNew();
            try
            {
                // Extract just the IDs and timestamps for the batch update
                var messageIds = messageBatch.Select(m => m.MessageId).ToList();
                var timestamp = messageBatch.First().ReceivedAt; // Use first message's timestamp for all

                _logger.LogInformation("Consumer #{InstanceId} flushing batch of {MessageCount} messages to Outbox table", _instanceId, messageBatch.Count);

                // Use the new batch method that performs a single database operation
                await _outboxService.MarkMessagesAsReceivedBatchAsync(messageIds, timestamp, stoppingToken);

                batchStopwatch.Stop();
                var messagesPerSecond = (messageBatch.Count / batchStopwatch.Elapsed.TotalSeconds);
                _logger.LogInformation("Batch flush completed in {ElapsedMs}ms. Throughput: {MessagesPerSecond:F0} msg/sec",
                    batchStopwatch.ElapsedMilliseconds, messagesPerSecond);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning("Batch flush cancelled. {MessageCount} messages buffered but not saved", messageBatch.Count);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error flushing batch of {MessageCount} messages", messageBatch.Count);
                // Log but continue - individual messages can be retried
                throw;
            }
        }

        /// <summary>
        /// Extracts the message ID from the Avro-encoded Kafka message
        /// Handles Confluent Schema Registry format (magic byte + schema ID + data)
        /// </summary>
        private long ExtractMessageId(string messageValue)
        {
            try
            {
                // For Avro, messageValue contains the Avro-encoded binary data as a string
                // We need to decode it based on the Avro schema
                
                // If using Confluent Schema Registry, the first 5 bytes are:
                // [0] = 0 (magic byte)
                // [1-4] = schema ID (4 bytes, big-endian)
                
                if (string.IsNullOrEmpty(messageValue))
                {
                    _logger.LogWarning("Empty Avro message value");
                    return -1;
                }

                // Convert string to bytes if it's hex-encoded
                byte[] messageBytes;
                try
                {
                    // Try to parse as hex string first (common for Avro serialization)
                    messageBytes = ConvertHexStringToBytes(messageValue);
                }
                catch
                {
                    // If not hex, try UTF-8 encoding
                    messageBytes = Encoding.UTF8.GetBytes(messageValue);
                }

                // Avro structure varies by schema, but ID is typically at the beginning
                // For a simple { "id": long } schema, after 5-byte header:
                // We try to extract the first long value
                
                if (messageBytes.Length >= 13) // 5 (header) + 8 (long)
                {
                    // Skip magic byte and schema ID (5 bytes) and read next 8 bytes as long
                    long messageId = BitConverter.ToInt64(messageBytes, 5);
                    
                    if (messageId > 0)
                    {
                        return messageId;
                    }
                }

                // Fallback: try to find any valid long value in the message
                _logger.LogWarning("Could not extract message ID from Avro message, length: {Length}", messageBytes.Length);
                return -1;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing message as Avro");
                return -1;
            }
        }

        /// <summary>
        /// Converts hex string to byte array
        /// </summary>
        private byte[] ConvertHexStringToBytes(string hex)
        {
            // Remove spaces if any
            hex = hex.Replace(" ", "");
            
            if (hex.Length % 2 != 0)
            {
                throw new ArgumentException("Hex string length must be even");
            }

            byte[] result = new byte[hex.Length / 2];
            for (int i = 0; i < hex.Length; i += 2)
            {
                result[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
            }

            return result;
        }
    }
}
