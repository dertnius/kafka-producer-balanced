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
    private readonly IKafkaService _kafkaService;
    private readonly IPublishBatchHandler _publishBatchHandler;
    private readonly ISchemaRegistryClient _schemaRegistryClient;
    private readonly IAsyncSerializer<GenericRecord> _avroSerializer;
    private readonly RecordSchema _outboxSchema;
    
    private readonly Channel<OutboxMessage> _processingChannel;
    private readonly ConcurrentBag<Task> _backgroundTasks;
    private readonly SemaphoreSlim _databaseSemaphore;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _mortgageStidLocks;
    private PerformanceMetrics _metrics;

    public OutboxProcessorServiceScaled(
        ILogger<OutboxProcessorServiceScaled> logger,
        IOptions<KafkaOutboxSettings> kafkaSettings,
        string connectionString,
        IKafkaService kafkaService,
        IPublishBatchHandler publishBatchHandler)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _kafkaSettings = kafkaSettings?.Value ?? throw new ArgumentNullException(nameof(kafkaSettings));
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _kafkaService = kafkaService ?? throw new ArgumentNullException(nameof(kafkaService));
        _publishBatchHandler = publishBatchHandler ?? throw new ArgumentNullException(nameof(publishBatchHandler));
        
        _backgroundTasks = new ConcurrentBag<Task>();
        _databaseSemaphore = new SemaphoreSlim(_kafkaSettings.DatabaseConnectionPoolSize);
        _mortgageStidLocks = new ConcurrentDictionary<string, SemaphoreSlim>();
        _processingChannel = Channel.CreateBounded<OutboxMessage>(
            new BoundedChannelOptions(_kafkaSettings.MaxProducerBuffer)
            {
                FullMode = BoundedChannelFullMode.Wait
            });
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
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Executing Scaled Kafka Outbox Processor Service");
        
        // Start background tasks
        var pollingTask = PollOutboxAsync(stoppingToken);
        var producingTask = ProduceMessagesAsync(stoppingToken);
        var metricsTask = ReportMetricsAsync(stoppingToken);

        await Task.WhenAll(pollingTask, producingTask, metricsTask);
    }

    /// <summary>
    /// Task 1: Poll database for unprocessed messages
    /// </summary>
    private async Task PollOutboxAsync(CancellationToken stoppingToken)
    {
        int currentPollingDelayMs = _kafkaSettings.PollingIntervalMs;
        int consecutiveEmptyPolls = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var messages = await GetUnprocessedMessagesAsync(stoppingToken);
                
                if (messages.Count == 0)
                {
                    consecutiveEmptyPolls++;
                    
                    // Adaptive backoff: increase delay when idle
                    if (_kafkaSettings.EnableAdaptiveBackoff && consecutiveEmptyPolls > 1)
                    {
                        currentPollingDelayMs = (int)Math.Min(
                            currentPollingDelayMs * _kafkaSettings.BackoffMultiplier,
                            _kafkaSettings.MaxPollingIntervalMs);
                        
                        _logger.LogDebug("No messages found. Adaptive backoff: polling delay increased to {DelayMs}ms", 
                            currentPollingDelayMs);
                    }

                    await Task.Delay(currentPollingDelayMs, stoppingToken);
                    continue;
                }

                // Reset to fast polling when work is found
                consecutiveEmptyPolls = 0;
                currentPollingDelayMs = _kafkaSettings.PollingIntervalMs;

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
                    
                    // Enqueue for batch publish status update
                    await _publishBatchHandler.EnqueueForPublishAsync(message.Id, stoppingToken);
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
            // Convert OutboxMessage to OutboxItem for KafkaService
            var outboxItem = new OutboxItem
            {
                Id = message.Id,
                Stid = message.MortgageStid,
                Code = message.SecPoolCode,
                Rank = message.Rank ?? 0,
                Processed = message.Processed,
                Retry = 0,
                ErrorCode = null
            };

            // Use KafkaService to produce the message
            await _kafkaService.ProduceMessageAsync(outboxItem, stoppingToken);

            _logger.LogDebug("Message {MessageId} produced to Kafka via KafkaService", message.Id);
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



    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Scaled Kafka Outbox Processor Service");
        _metrics.LogMetrics(_logger);
        
        await base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        _databaseSemaphore?.Dispose();
        _processingChannel?.Writer.Complete();
        
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

