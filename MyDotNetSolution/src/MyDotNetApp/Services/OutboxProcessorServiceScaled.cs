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
    private readonly ConcurrentDictionary<string, long> _stidLastUsed;
    private readonly ConcurrentDictionary<long, byte> _inFlightMessages; // Track messages already in channel/processing
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
        _stidLastUsed = new ConcurrentDictionary<string, long>();
        _inFlightMessages = new ConcurrentDictionary<long, byte>(); // byte is dummy value (minimal memory)
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
        _logger.LogInformation("OutboxProcessorServiceScaled is starting");
        
        try
        {
            // Start background tasks
            var pollingTask = PollOutboxAsync(stoppingToken);
            var producingTask = ProduceMessagesAsync(stoppingToken);
            var cleanupTask = CleanupStidLocksAsync(stoppingToken);
            var metricsTask = ReportMetricsAsync(stoppingToken);

            await Task.WhenAll(pollingTask, producingTask, metricsTask, cleanupTask);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("OutboxProcessorServiceScaled cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in OutboxProcessorServiceScaled - service will attempt to restart");
            // Don't rethrow - let host manager restart if needed
        }
    }

    /// <summary>
    /// Task 1: Poll database for unprocessed messages with smart backoff
    /// Only polls when messages are being processed, backs off when idle
    /// </summary>
    private async Task PollOutboxAsync(CancellationToken stoppingToken)
    {
        int currentPollingDelayMs = _kafkaSettings.PollingIntervalMs;
        int consecutiveEmptyPolls = 0;
        var lastPollTime = DateTime.UtcNow;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Check if processing channel has capacity before polling
                // If channel is full, skip this poll to back off naturally
                var channelCount = _processingChannel.Reader.Count;
                var channelCapacity = _kafkaSettings.MaxProducerBuffer;
                
                if (channelCount > channelCapacity * 0.8)  // 80% full, back off
                {
                    _logger.LogDebug("Processing channel at {Percent}% capacity, backing off", 
                        (channelCount * 100) / channelCapacity);
                    await Task.Delay(Math.Min(currentPollingDelayMs * 2, _kafkaSettings.MaxPollingIntervalMs), stoppingToken);
                    continue;
                }

                var messages = await GetUnprocessedMessagesAsync(stoppingToken);
                var pollDuration = DateTime.UtcNow - lastPollTime;
                lastPollTime = DateTime.UtcNow;
                
                if (messages.Count == 0)
                {
                    consecutiveEmptyPolls++;
                    
                    // Adaptive backoff: increase delay exponentially when idle
                    if (_kafkaSettings.EnableAdaptiveBackoff)
                    {
                        currentPollingDelayMs = (int)Math.Min(
                            currentPollingDelayMs * _kafkaSettings.BackoffMultiplier,
                            _kafkaSettings.MaxPollingIntervalMs);
                        
                        _logger.LogDebug(
                            "No messages found (poll #{EmptyCount}). Backing off to {DelayMs}ms", 
                            consecutiveEmptyPolls, currentPollingDelayMs);
                    }

                    await Task.Delay(currentPollingDelayMs, stoppingToken);
                    continue;
                }

                // Reset backoff when work is found
                consecutiveEmptyPolls = 0;
                currentPollingDelayMs = _kafkaSettings.PollingIntervalMs;

                _logger.LogDebug("Fetched {MessageCount} messages from outbox (query took {Duration}ms)", 
                    messages.Count, pollDuration.TotalMilliseconds);
                _metrics.RecordFetched(messages.Count);

                // Add messages to processing channel, skipping duplicates already in-flight
                int addedCount = 0;
                int skippedCount = 0;
                foreach (var message in messages)
                {
                    // Check if message is already in channel or being processed
                    if (_inFlightMessages.TryAdd(message.Id, 0))
                    {
                        await _processingChannel.Writer.WriteAsync(message, stoppingToken);
                        addedCount++;
                    }
                    else
                    {
                        skippedCount++;
                        _logger.LogDebug("Skipped duplicate message {MessageId} already in flight", message.Id);
                    }
                }
                
                if (skippedCount > 0)
                {
                    _logger.LogInformation("Added {AddedCount} new messages, skipped {SkippedCount} duplicates already in channel", 
                        addedCount, skippedCount);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error polling outbox");
                // Increase delay on error to avoid hammering DB
                currentPollingDelayMs = Math.Min(currentPollingDelayMs * 2, _kafkaSettings.MaxPollingIntervalMs);
                await Task.Delay(currentPollingDelayMs, stoppingToken);
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
                _stidLastUsed[stidKey] = Environment.TickCount64;
                await stidLock.WaitAsync(stoppingToken);
                try
                {
                    await SendMessageToKafkaAsync(message, stoppingToken);
                    _metrics.RecordProduced();
                    
                    // Enqueue for batch publish status update
                    await _publishBatchHandler.EnqueueForPublishAsync(message.Id, stoppingToken);
                    
                    // Success: Remove from in-flight tracking
                    _inFlightMessages.TryRemove(message.Id, out _);
                }
                catch (Exception publishEx)
                {
                    // CRITICAL: Remove from in-flight so message can be re-polled
                    _logger.LogError(publishEx, "Failed to publish message {MessageId}, will retry on next poll", message.Id);
                    _inFlightMessages.TryRemove(message.Id, out _); // Allow retry
                    _metrics.RecordFailed();
                    throw; // Rethrow to be caught by outer catch
                }
                finally
                {
                    stidLock.Release();
                    _stidLastUsed[stidKey] = Environment.TickCount64;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error producing message {MessageId}, will retry on next poll", message.Id);
                _metrics.RecordFailed();
            }
        }
    }



    /// <summary>
    /// Report performance metrics and clean up stale locks every 10 seconds
    /// </summary>
    private async Task ReportMetricsAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(10000, stoppingToken);
                _metrics.LogMetrics(_logger);
                
                // Clean up stale STID locks to prevent memory leak
                CleanupStaleSemaphores();
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

    /// <summary>
    /// Remove semaphores for STIDs that haven't been used in a while
    /// Uses actual idle time, not alphabetical order
    /// </summary>
    private void CleanupStaleSemaphores()
    {
        const int maxLocksToKeep = 5000;   // More aggressive: keep only 5k
        const long idleThresholdMs = 10000;  // Remove if idle > 10 seconds (not 120s)
        
        var now = Environment.TickCount64;
        
        // Clean by idle time, not alphabetical order
        var staleKeys = _stidLastUsed
            .Where(kvp => (now - kvp.Value) > idleThresholdMs)
            .Select(kvp => kvp.Key)
            .ToList();
        
        // Remove idle locks
        foreach (var stidKey in staleKeys)
        {
            if (_mortgageStidLocks.TryRemove(stidKey, out var semaphore))
            {
                var ageMs = _stidLastUsed.TryGetValue(stidKey, out var lastUsed) ? now - lastUsed : 0;
                semaphore?.Dispose();
                _stidLastUsed.TryRemove(stidKey, out _);
                _logger.LogDebug("Cleaned up idle semaphore for STID: {Stid} (age: {AgeMs}ms)", 
                    stidKey, ageMs);
            }
        }
        
        // If still over limit, force removal of oldest unused
        if (_mortgageStidLocks.Count > maxLocksToKeep)
        {
            var excess = _mortgageStidLocks.Count - maxLocksToKeep;
            var forcedRemoval = _stidLastUsed
                .OrderBy(x => x.Value)  // Oldest first by actual usage time
                .Take(excess)
                .Select(x => x.Key)
                .ToList();
            
            foreach (var stidKey in forcedRemoval)
            {
                if (_mortgageStidLocks.TryRemove(stidKey, out var semaphore))
                {
                    semaphore?.Dispose();
                    _stidLastUsed.TryRemove(stidKey, out _);
                    _logger.LogWarning("Forced removal of semaphore for STID: {Stid} (memory pressure)", stidKey);
                }
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
                await connection.OpenAsync(stoppingToken);
                
                // Get oldest unpublished message per MortgageStid to maintain ordering
                // In-flight tracking prevents duplicates (no DB locking needed)
                const string sql = @"
                    WITH Candidate AS (
                        SELECT
                            Id,
                            AggregateId,
                            EventType,
                            CAST(0 AS bit) AS Processed,
                            CreatedAt,
                            ProcessedAt,
                            Rank,
                            SecType,
                            Domain,
                            MortgageStid,
                            SecPoolCode,
                            Publish,
                            ProducedAt,
                            ReceivedAt,
                            ROW_NUMBER() OVER (PARTITION BY MortgageStid ORDER BY Id) AS rn
                        FROM Outbox WITH (READPAST)
                        WHERE Publish = 0
                    )
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
                    FROM Candidate
                    WHERE rn = 1
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

    // Periodically clean up STID locks that haven't been used recently
    private async Task CleanupStidLocksAsync(CancellationToken stoppingToken)
    {
        const int cleanupIntervalMs = 60000; // 60s
        const long idleThresholdMs = 120000; // 120s
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(cleanupIntervalMs, stoppingToken);
                var now = Environment.TickCount64;
                foreach (var kvp in _stidLastUsed.ToArray())
                {
                    var idleMs = now - kvp.Value;
                    if (idleMs > idleThresholdMs && _mortgageStidLocks.TryGetValue(kvp.Key, out var sem))
                    {
                        if (sem.CurrentCount == 1)
                        {
                            if (_mortgageStidLocks.TryRemove(kvp.Key, out var removed))
                            {
                                removed.Dispose();
                                _stidLastUsed.TryRemove(kvp.Key, out _);
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during STID locks cleanup");
            }
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

