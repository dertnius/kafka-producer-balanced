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
using MyDotNetApp.Diagnostics;

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
    
    private readonly Channel<OutboxMessageLegacy> _processingChannel;
    private readonly ConcurrentBag<Task> _backgroundTasks;
    private readonly SemaphoreSlim _databaseSemaphore;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _mortgageStidLocks;
    private readonly ConcurrentDictionary<string, long> _stidLastUsed;
    private readonly ConcurrentDictionary<long, long> _inFlightMessages; // Track messages with timestamp (TickCount64)
    private CancellationToken _stoppingToken;
    private long _manualTriggerCount;
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
        _inFlightMessages = new ConcurrentDictionary<long, long>(); // Track with timestamp for cleanup
        _processingChannel = Channel.CreateBounded<OutboxMessageLegacy>(
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
        _metrics.LogMemoryStartup(_logger);
        await base.StartAsync(cancellationToken);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Return immediately - run all work on background thread pool
        return Task.Run(async () =>
        {
            // Wait a bit to ensure web app is fully started
            await Task.Delay(100, stoppingToken);
            
            _logger.LogInformation("OutboxProcessorServiceScaled is starting");
            _stoppingToken = stoppingToken;
            
            try
            {
                // Start all background tasks
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
                _logger.LogError(ex, "Unexpected error in OutboxProcessorServiceScaled");
            }
        }, stoppingToken);
    }

    private void EnsureProcessingLoop()
    {
        // No longer needed - ExecuteAsync handles everything
    }

    /// <summary>
    /// Task 1: Poll database for unprocessed messages on a fixed timer interval
    /// Polls every PollingIntervalMs (default 100ms)
    /// </summary>
    protected internal virtual async Task PollOutboxAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Wait for polling interval
                await Task.Delay(_kafkaSettings.PollingIntervalMs, stoppingToken);

                using var activity = Telemetry.ActivitySource.StartActivity("PollOutbox");
                
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var messages = await GetUnprocessedMessagesAsync(stoppingToken);
                sw.Stop();
                
                activity?.SetTag("messages.count", messages.Count);
                activity?.SetTag("query.duration_ms", sw.Elapsed.TotalMilliseconds);
                
                if (messages.Count == 0)
                {
                    // No messages, continue to next timer tick
                    continue;
                }

                _logger.LogDebug("Fetched {MessageCount} messages from outbox (query took {Duration}ms)", 
                    messages.Count, sw.Elapsed.TotalMilliseconds);
                _metrics.RecordFetched(messages.Count);

                // Add messages to processing channel, skipping duplicates already in-flight
                int addedCount = 0;
                int skippedCount = 0;
                foreach (var message in messages)
                {
                    // Check if message is already in channel or being processed
                    // Store timestamp for cleanup tracking
                    if (_inFlightMessages.TryAdd(message.Id, Environment.TickCount64))
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
                
                activity?.SetTag("messages.added", addedCount);
                activity?.SetTag("messages.skipped", skippedCount);
                
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
                // Wait before retrying on error
                await Task.Delay(_kafkaSettings.PollingIntervalMs * 5, stoppingToken);
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
                _metrics.Reset(); // Reset metrics after logging to prevent memory accumulation
                
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

    protected virtual async Task<List<OutboxMessageLegacy>> GetUnprocessedMessagesAsync(CancellationToken stoppingToken)
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
                
                var messages = await connection.QueryAsync<OutboxMessageLegacy>(
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

    private async Task SendMessageToKafkaAsync(OutboxMessageLegacy message, CancellationToken stoppingToken)
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
        const long idleThresholdMs = 120000; // 120s (2 minutes) - STID locks
        const long stuckMessageThresholdMs = 1800000; // 1800s (30 minutes) - safety net only
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(cleanupIntervalMs, stoppingToken);
                var now = Environment.TickCount64;
                
                // Cleanup idle STID locks
                var removedLocks = 0;
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
                                removedLocks++;
                            }
                        }
                    }
                }
                
                // SAFETY NET: Remove truly stuck messages (should never happen in normal operation)
                // Messages are removed immediately on success/failure, so only a worker hang would leave them
                var stuckCount = 0;
                foreach (var kvp in _inFlightMessages.ToArray())
                {
                    var messageId = kvp.Key;
                    var addedTime = kvp.Value;
                    var ageMs = now - addedTime;
                    
                    // 30 minutes is long enough to avoid false positives during backlog
                    // but will catch genuinely hung workers
                    if (ageMs > stuckMessageThresholdMs)
                    {
                        if (_inFlightMessages.TryRemove(messageId, out _))
                        {
                            stuckCount++;
                            _logger.LogError(
                                "Removed stuck message {MessageId} from in-flight tracking (age: {AgeMinutes:F1} min) - possible worker hang",
                                messageId, ageMs / 60000.0);
                        }
                    }
                }
                
                // Log memory stats every cleanup cycle
                var inFlightCount = _inFlightMessages.Count;
                var stidLockCount = _mortgageStidLocks.Count;
                
                if (removedLocks > 0)
                {
                    _logger.LogInformation("Cleaned up {RemovedLocks} idle STID locks", removedLocks);
                }
                
                if (stuckCount > 0)
                {
                    _logger.LogError("ALERT: Cleaned up {StuckCount} stuck messages - investigate worker health", stuckCount);
                }
                
                // Alert if in-flight count is abnormally high
                if (inFlightCount > 50000)
                {
                    _logger.LogWarning(
                        "High in-flight message count: {InFlightCount} (channel backlog or slow processing)",
                        inFlightCount);
                }
                
                _logger.LogDebug(
                    "Memory stats: {InFlightCount} in-flight messages, {StidLockCount} STID locks",
                    inFlightCount, stidLockCount);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during cleanup");
            }
        }
    }



    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Scaled Kafka Outbox Processor Service");
        _metrics.LogMetrics(_logger);
        _metrics.LogMemoryShutdown(_logger);
        
        await base.StopAsync(cancellationToken);
    }

    /// <summary>
    /// Directly poll and process messages - call from API endpoint for immediate processing
    /// Returns count of messages added to processing channel
    /// </summary>
    public async Task<int> TriggerPollAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Direct poll requested via API");
        Interlocked.Increment(ref _manualTriggerCount);
        
        var messages = await GetUnprocessedMessagesAsync(cancellationToken);
        if (messages.Count == 0)
        {
            _logger.LogInformation("No messages to process");
            return 0;
        }

        // Add to processing channel
        int addedCount = 0;
        foreach (var message in messages)
        {
            if (_inFlightMessages.TryAdd(message.Id, Environment.TickCount64))
            {
                await _processingChannel.Writer.WriteAsync(message, cancellationToken);
                addedCount++;
            }
        }
        
        _logger.LogInformation("Added {Count} messages for processing", addedCount);
        return addedCount;
    }

    /// <summary>
    /// Get current processing statistics
    /// </summary>
    public object GetProcessingStats()
    {
        return new
        {
            inFlightMessages = _inFlightMessages.Count,
            stidLocks = _mortgageStidLocks.Count,
            manualTriggers = Interlocked.Read(ref _manualTriggerCount),
            metrics = new
            {
                timestamp = DateTime.UtcNow
            }
        };
    }

    public long ManualTriggerCount => Interlocked.Read(ref _manualTriggerCount);

    public override void Dispose()
    {
        _databaseSemaphore?.Dispose();
        if (_processingChannel != null)
        {
            // Defensive: avoid throwing if already completed elsewhere
            _processingChannel.Writer.TryComplete();
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

