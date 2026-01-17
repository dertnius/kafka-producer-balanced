using Confluent.Kafka;
using Dapper;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyDotNetApp.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.SqlClient;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Avro;
using Avro.Generic;
using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;

namespace MyDotNetApp.Services;

public class OutboxProcessorServiceScaled : BackgroundService
{
    private readonly ILogger<OutboxProcessorServiceScaled> _logger;
    private readonly string _connectionString;
    private readonly KafkaOutboxSettings _kafkaSettings;
    private readonly List<IProducer<string, byte[]>> _producers;
    private readonly ISchemaRegistryClient _schemaRegistryClient;
    private readonly IAsyncSerializer<GenericRecord> _avroSerializer;
    private readonly RecordSchema _outboxSchema;
    private int _producerRoundRobin = 0;
    
    private readonly Channel<OutboxMessage> _processingChannel;
    private readonly Channel<long> _producedMessageIdsChannel;  // NEW: Track successfully produced messages
    private readonly ConcurrentBag<Task> _backgroundTasks;
    private readonly SemaphoreSlim _databaseSemaphore;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _mortgageStidLocks;
    private PerformanceMetrics _metrics;

    public OutboxProcessorServiceScaled(
        ILogger<OutboxProcessorServiceScaled> logger,
        IOptions<KafkaOutboxSettings> kafkaSettings,
        string connectionString)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _kafkaSettings = kafkaSettings?.Value ?? throw new ArgumentNullException(nameof(kafkaSettings));
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        
        _producers = new List<IProducer<string, byte[]>>(_kafkaSettings.MaxConcurrentProducers);
        _backgroundTasks = new ConcurrentBag<Task>();
        _databaseSemaphore = new SemaphoreSlim(_kafkaSettings.DatabaseConnectionPoolSize);
        _mortgageStidLocks = new ConcurrentDictionary<string, SemaphoreSlim>();
        _processingChannel = Channel.CreateBounded<OutboxMessage>(
            new BoundedChannelOptions(_kafkaSettings.MaxProducerBuffer)
            {
                FullMode = BoundedChannelFullMode.Wait
            });
        // Channel for tracking successfully produced message IDs
        _producedMessageIdsChannel = Channel.CreateUnbounded<long>(
            new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });
        _metrics = new PerformanceMetrics();

        if (string.IsNullOrWhiteSpace(_kafkaSettings.SchemaRegistryUrl))
        {
            throw new ArgumentNullException(nameof(_kafkaSettings.SchemaRegistryUrl), "SchemaRegistryUrl is required for Avro serialization");
        }

        var schemaRegistryConfig = new SchemaRegistryConfig
        {
            Url = _kafkaSettings.SchemaRegistryUrl
        };

        _schemaRegistryClient = new CachedSchemaRegistryClient(schemaRegistryConfig);
        _outboxSchema = (RecordSchema)Avro.Schema.Parse(@"{
            ""type"": ""record"",
            ""name"": ""OutboxMessage"",
            ""namespace"": ""MyDotNetApp"",
            ""fields"": [
                {""name"": ""Id"", ""type"": ""long""},
                {""name"": ""AggregateId"", ""type"": [""null"", ""string""], ""default"": null},
                {""name"": ""EventType"", ""type"": [""null"", ""string""], ""default"": null},
                {""name"": ""Rank"", ""type"": [""null"", ""int""], ""default"": null},
                {""name"": ""SecType"", ""type"": [""null"", ""string""], ""default"": null},
                {""name"": ""Domain"", ""type"": [""null"", ""string""], ""default"": null},
                {""name"": ""MortgageStid"", ""type"": [""null"", ""string""], ""default"": null},
                {""name"": ""SecPoolCode"", ""type"": [""null"", ""string""], ""default"": null},
                {""name"": ""Publish"", ""type"": ""boolean""},
                {""name"": ""ProducedAt"", ""type"": [""null"", {""type"": ""long"", ""logicalType"": ""timestamp-millis""}], ""default"": null},
                {""name"": ""ReceivedAt"", ""type"": [""null"", {""type"": ""long"", ""logicalType"": ""timestamp-millis""}], ""default"": null},
                {""name"": ""Processed"", ""type"": ""boolean""},
                {""name"": ""CreatedAt"", ""type"": [""null"", {""type"": ""long"", ""logicalType"": ""timestamp-millis""}], ""default"": null},
                {""name"": ""ProcessedAt"", ""type"": [""null"", {""type"": ""long"", ""logicalType"": ""timestamp-millis""}], ""default"": null}
            ]
        }");

        _avroSerializer = new AvroSerializer<GenericRecord>(_schemaRegistryClient);
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting Scaled Kafka Outbox Processor Service");
        
        try
        {
            InitializeKafkaProducers();
            await base.StartAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting Scaled Kafka Outbox Processor Service");
            throw;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Executing Scaled Kafka Outbox Processor Service");
        
        // Start background tasks
        var pollingTask = PollOutboxAsync(stoppingToken);
        var producingTask = ProduceMessagesAsync(stoppingToken);
        var markingTask = MarkMessagesAsPublishedAsync(stoppingToken);
        var metricsTask = ReportMetricsAsync(stoppingToken);

        await Task.WhenAll(pollingTask, producingTask, markingTask, metricsTask);
    }

    /// <summary>
    /// Task 1: Poll database for unprocessed messages
    /// </summary>
    private async Task PollOutboxAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var messages = await GetUnprocessedMessagesAsync(stoppingToken);
                
                if (messages.Count == 0)
                {
                    await Task.Delay(_kafkaSettings.PollingIntervalMs, stoppingToken);
                    continue;
                }

                _logger.LogDebug("Fetched {MessageCount} messages from outbox", messages.Count);
                _metrics.RecordFetched(messages.Count);

                // Add messages to processing channel
                foreach (var message in messages)
                {
                    await _processingChannel.Writer.WriteAsync(message, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error polling outbox");
                await Task.Delay(1000, stoppingToken);
            }
        }

        _processingChannel.Writer.Complete();
    }

    /// <summary>
    /// Task 2: Produce messages to Kafka (multiple concurrent producers)
    /// </summary>
    private async Task ProduceMessagesAsync(CancellationToken stoppingToken)
    {
        var producerTasks = new List<Task>();
        
        // Create multiple concurrent producer tasks
        for (int i = 0; i < _kafkaSettings.MaxConcurrentProducers; i++)
        {
            producerTasks.Add(ProducerWorkerAsync(stoppingToken));
        }

        await Task.WhenAll(producerTasks);
    }

    private async Task ProducerWorkerAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in _processingChannel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                // Ensure ordering per MortgageStid - only one message per MortgageStid can be sent at a time
                var stidKey = message.MortgageStid ?? string.Empty;
                var stidLock = _mortgageStidLocks.GetOrAdd(stidKey, _ => new SemaphoreSlim(1, 1));
                
                await stidLock.WaitAsync(stoppingToken);
                try
                {
                    await SendMessageToKafkaAsync(message, stoppingToken);
                    _metrics.RecordProduced();
                    
                    // Send successfully produced message ID to the marking channel
                    await _producedMessageIdsChannel.Writer.WriteAsync(message.Id, stoppingToken);
                }
                finally
                {
                    stidLock.Release();
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error producing message {MessageId}", message.Id);
                _metrics.RecordFailed();
            }
        }
    }

    /// <summary>
    /// Task 3: Mark messages as published in batches (batched updates are faster)
    /// </summary>
    private async Task MarkMessagesAsPublishedAsync(CancellationToken stoppingToken)
    {
        var batch = new List<long>(10000);
        var timer = Stopwatch.StartNew();
        const int batchFlushIntervalMs = 1000; // Flush every 1 second

        try
        {
            await foreach (var messageId in _producedMessageIdsChannel.Reader.ReadAllAsync(stoppingToken))
            {
                batch.Add(messageId);
                
                // Flush batch when it reaches 10,000 items or every 1 second
                if (batch.Count >= 10000 || (timer.ElapsedMilliseconds > batchFlushIntervalMs && batch.Count > 0))
                {
                    await MarkBatchAsPublishedAsync(batch, stoppingToken);
                    _metrics.RecordMarked(batch.Count);
                    batch.Clear();
                    timer.Restart();
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Flush remaining batch on cancellation
            if (batch.Count > 0)
            {
                await MarkBatchAsPublishedAsync(batch, stoppingToken);
                _metrics.RecordMarked(batch.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking messages as published");
        }
    }

    /// <summary>
    /// Report performance metrics every 10 seconds
    /// </summary>
    private async Task ReportMetricsAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(10000, stoppingToken);
                _metrics.LogMetrics(_logger);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reporting metrics");
            }
        }
    }

    private async Task<List<OutboxMessage>> GetUnprocessedMessagesAsync(CancellationToken stoppingToken)
    {
        await _databaseSemaphore.WaitAsync(stoppingToken);
        
        try
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                // Use connection pooling - don't close between calls
                await connection.OpenAsync(stoppingToken);
                
                const string sql = @"
                    SELECT TOP (@BatchSize)
                        Id,
                        AggregateId,
                        EventType,
                        Processed,
                        CreatedAt,
                        ProcessedAt,
                        Rank,
                        SecType,
                        Domain,
                        MortgageStid,
                        SecPoolCode,
                        Publish,
                        ProducedAt,
                        ReceivedAt
                    FROM Outbox WITH (NOLOCK)
                    WHERE Publish = 0 AND Processed = 1
                    ORDER BY MortgageStid ASC, Id ASC";

                var messages = await connection.QueryAsync<OutboxMessage>(
                    sql,
                    new { BatchSize = _kafkaSettings.BatchSize });

                return messages.ToList();
            }
        }
        finally
        {
            _databaseSemaphore.Release();
        }
    }

    private static long? ToUnixMillis(DateTime? dt)
    {
        return dt.HasValue ? new DateTimeOffset(dt.Value).ToUnixTimeMilliseconds() : (long?)null;
    }

    private async Task SendMessageToKafkaAsync(OutboxMessage message, CancellationToken stoppingToken)
    {
        try
        {
            // Round-robin through producers for load balancing
            var producer = _producers[Interlocked.Increment(ref _producerRoundRobin) % _producers.Count];

            // Build Avro GenericRecord
            var record = new GenericRecord(_outboxSchema);
            record.Add("Id", message.Id);
            record.Add("AggregateId", message.AggregateId);
            record.Add("EventType", message.EventType);
            record.Add("Rank", message.Rank);
            record.Add("SecType", message.SecType);
            record.Add("Domain", message.Domain);
            record.Add("MortgageStid", message.MortgageStid);
            record.Add("SecPoolCode", message.SecPoolCode);
            record.Add("Publish", message.Publish);
            record.Add("ProducedAt", ToUnixMillis(message.ProducedAt));
            record.Add("ReceivedAt", ToUnixMillis(message.ReceivedAt));
            record.Add("Processed", message.Processed);
            record.Add("CreatedAt", ToUnixMillis(message.CreatedAt));
            record.Add("ProcessedAt", ToUnixMillis(message.ProcessedAt));

            var avroBytes = await _avroSerializer.SerializeAsync(
                record,
                new SerializationContext(MessageComponentType.Value, _kafkaSettings.TopicName));

            var kafkaMessage = new Message<string, byte[]>
            {
                Key = message.MortgageStid ?? string.Empty,
                Value = avroBytes,
                Headers = new Headers
                {
                    { "event-type", Encoding.UTF8.GetBytes(message.EventType ?? string.Empty) }
                }
            };

            await producer.ProduceAsync(
                _kafkaSettings.TopicName,
                kafkaMessage,
                stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send message to Kafka for message ID: {MessageId}", message.Id);
            throw;
        }
    }

    private async Task MarkBatchAsPublishedAsync(List<long> messageIds, CancellationToken stoppingToken)
    {
        if (messageIds.Count == 0) return;

        await _databaseSemaphore.WaitAsync(stoppingToken);
        
        try
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync(stoppingToken);
                
                // Use table-valued parameter for better performance with large batches
                // This avoids SQL Server lock escalation that happens with huge IN clauses
                var dataTable = new DataTable();
                dataTable.Columns.Add("Id", typeof(long));
                foreach (var id in messageIds)
                {
                    dataTable.Rows.Add(id);
                }

                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = @"
                        UPDATE o
                        SET o.Publish = 1,
                            o.ProducedAt = GETUTCDATE()
                        FROM Outbox o WITH (UPDLOCK)
                        INNER JOIN (
                            SELECT Value FROM STRING_SPLIT(@Ids, ',')
                        ) ids ON CAST(ids.Value AS BIGINT) = o.Id";
                    
                    cmd.Parameters.AddWithValue("@Ids", string.Join(",", messageIds));
                    cmd.CommandTimeout = 60;
                    
                    await cmd.ExecuteNonQueryAsync(stoppingToken);
                }
            }
        }
        finally
        {
            _databaseSemaphore.Release();
        }
    }

    private void InitializeKafkaProducers()
    {
        var producerConfig = new ProducerConfig
        {
            BootstrapServers = _kafkaSettings.BootstrapServers,
            Acks = Acks.Leader,
            CompressionType = CompressionType.Snappy,
            RetryBackoffMs = 50,
            MessageSendMaxRetries = 2,
            SocketKeepaliveEnable = true,
            SocketNagleDisable = true,  // Disable Nagle for lower latency
            LingerMs = 10,  // Reduced from 100ms for faster flushing at high throughput
            BatchSize = 1000000,  // 1MB batches
            QueueBufferingMaxMessages = 200000,  // Increased from 100k for burst capacity
            RequestTimeoutMs = 30000,
            DeliveryReportFields = "none"  // Skip delivery reports if not needed for speed
        };

        for (int i = 0; i < _kafkaSettings.MaxConcurrentProducers; i++)
        {
            var producer = new ProducerBuilder<string, byte[]>(producerConfig)
                .SetErrorHandler((_, error) =>
                {
                    _logger.LogError("Kafka producer error: {Error}", error.Reason);
                })
                .Build();

            _producers.Add(producer);
        }

        _logger.LogInformation("Initialized {ProducerCount} Kafka producers", _producers.Count);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Scaled Kafka Outbox Processor Service");
        _metrics.LogMetrics(_logger);
        
        // Signal that no more messages will be produced
        _producedMessageIdsChannel.Writer.Complete();
        
        try
        {
            foreach (var producer in _producers)
            {
                producer?.Flush(TimeSpan.FromSeconds(30));
                producer?.Dispose();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error flushing/disposing Kafka producers");
        }

        await base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        _databaseSemaphore?.Dispose();
        _processingChannel?.Writer.Complete();
        _producedMessageIdsChannel?.Writer.Complete();  // Complete the channel
        
        foreach (var producer in _producers)
        {
            producer?.Dispose();
        }
        
        // Dispose all MortgageStid locks
        foreach (var kvp in _mortgageStidLocks)
        {
            kvp.Value?.Dispose();
        }
        _mortgageStidLocks.Clear();

        _schemaRegistryClient?.Dispose();
        
        base.Dispose();
    }
}

