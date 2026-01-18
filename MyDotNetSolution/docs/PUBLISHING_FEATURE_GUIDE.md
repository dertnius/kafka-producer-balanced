# Message Publishing Feature Guide

## Overview

The OutboxMessage now includes comprehensive publishing tracking with high-performance batch operations optimized for concurrent message processing and Kafka publishing.

## Fields

### Core Message Fields
- **Id** (BIGINT): Primary message identifier
- **AggregateId** (NVARCHAR): Aggregate root identifier  
- **EventType** (NVARCHAR): Type of event
- **Payload** (NVARCHAR MAX): Event payload/data
- **Rank** (INT): Message rank/priority

### Domain-Specific Fields
- **SecType** (NVARCHAR(2)): Security type code
- **Domain** (NVARCHAR(26)): Domain identifier
- **MortgageStid** (NVARCHAR(56)): Mortgage STID reference
- **SecPoolCode** (NVARCHAR(4)): Security pool code

### Publishing Status Fields
- **Publish** (BIT): Flag indicating if message has been published to primary topic
- **ProducedAt** (DATETIME2): Timestamp when message was produced to Kafka
- **ReceivedAt** (DATETIME2): Timestamp when message was received from other topic

### Processing Status Fields
- **Processed** (BIT): Flag indicating message processing completion
- **CreatedAt** (DATETIME2): Message creation timestamp
- **ProcessedAt** (DATETIME2): Message processing completion timestamp
- **Retry** (INT): Retry attempt count
- **ErrorCode** (NVARCHAR): Error code from failed operations

## Architecture

### High-Performance Batch Publishing

```
Message Processing Flow:
    ↓
ProduceToKafka()
    ↓
EnqueueForPublish(messageId)  ← Adds to batch
    ↓
Batch Accumulator (IPublishBatchHandler)
    ↓
[Auto-flush on 5000 msgs OR Timer (1000ms)]
    ↓
MarkMessagesAsPublishedBatch()  ← Parameterized bulk UPDATE
    ↓
Database (Outbox.Publish = 1)
```

### Components

#### IPublishBatchHandler
Accumulates message IDs and performs bulk updates:
```csharp
Task EnqueueForPublishAsync(long messageId, CancellationToken cancellationToken);
Task FlushAsync(CancellationToken cancellationToken);
```

#### PublishBatchHandler
- Accumulates up to 5000 message IDs
- Auto-flushes when batch is full
- Uses parameterized SQL for security
- Performance: ~10,000-50,000 msgs/sec depending on database

#### PublishFlushBackgroundService
- Background service that periodically flushes pending updates
- Ensures no messages get stuck in queue
- Final flush on application shutdown

## Usage

### Publishing a Message

```csharp
public class MessagingService : BackgroundService
{
    private readonly IKafkaService _kafkaService;
    private readonly IPublishBatchHandler _publishBatchHandler;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var message = new OutboxMessage { Id = 123, /* ... */ };

        // Produce to Kafka
        await _kafkaService.ProduceMessageAsync(message, stoppingToken);

        // Enqueue for batch publish update
        await _publishBatchHandler.EnqueueForPublishAsync(message.Id, stoppingToken);
    }
}
```

### Direct Publish Operations

```csharp
var outboxService = serviceProvider.GetRequiredService<IOutboxService>();

// Single message
await outboxService.MarkMessageAsPublishedAsync(messageId, cancellationToken);

// Batch operation (manual)
var ids = new List<long> { 1, 2, 3, 4, 5 };
await outboxService.MarkMessagesAsPublishedBatchAsync(ids, cancellationToken);

// Track production timestamp
await outboxService.MarkMessageAsProducedAsync(messageId, DateTime.UtcNow, cancellationToken);

// Track receipt timestamp
await outboxService.MarkMessageAsReceivedAsync(messageId, DateTime.UtcNow, cancellationToken);
```

## Configuration

### Development (appsettings.json)
```json
{
  "Publishing": {
    "BatchSize": 5000,
    "FlushIntervalMs": 1000
  }
}
```

### Production (appsettings.Production.json)
```json
{
  "Publishing": {
    "BatchSize": 10000,
    "FlushIntervalMs": 500
  }
}
```

### Tuning Parameters

| Parameter | Default | Dev | Prod | Notes |
|-----------|---------|-----|------|-------|
| BatchSize | 5000 | 5000 | 10000 | Increase for better throughput |
| FlushIntervalMs | 1000 | 1000 | 500 | Reduce for lower latency |

## Performance Characteristics

### Throughput

**Single Update:**
- ~500-1000 msgs/sec (individual UPDATE statements)

**Batch Update (5000 msgs):**
- ~10,000-50,000 msgs/sec (depends on network and disk I/O)

**Improvement Factor:**
- 10-50x faster than individual updates

### Database Impact

| Operation | Network Calls | Lock Duration | IO Operations |
|-----------|--------------|---------------|---|
| Single UPDATE | 1 per msg | Short | High (total) |
| Batch UPDATE | 1 per 5000 msgs | Medium | Low (total) |

### Memory Usage
- Accumulator: ~40 bytes per queued message ID
- Example: 10,000 queued IDs = ~400 KB

## Monitoring & Metrics

### Key Metrics

```sql
-- Messages published per minute
SELECT 
    DATEPART(MINUTE, ProducedAt) as Minute,
    COUNT(*) as PublishedCount
FROM Outbox
WHERE ProducedAt > DATEADD(MINUTE, -60, GETUTCDATE())
GROUP BY DATEPART(MINUTE, ProducedAt);

-- Unpublished message backlog
SELECT COUNT(*) as UnpublishedCount
FROM Outbox
WHERE Publish = 0;

-- Publication lag (time from created to produced)
SELECT 
    AVG(DATEDIFF(SECOND, CreatedAt, ProducedAt)) as AvgLagSeconds,
    MAX(DATEDIFF(SECOND, CreatedAt, ProducedAt)) as MaxLagSeconds
FROM Outbox
WHERE ProducedAt IS NOT NULL;
```

### Health Checks

```csharp
// Create health check endpoint
app.MapGet("/health/publish", async (IOutboxService service) =>
{
    var unpublished = await service.GetUnpublishedCountAsync();
    return new 
    { 
        status = unpublished > 100000 ? "degraded" : "healthy",
        unpublishedCount = unpublished
    };
});
```

## Error Handling

### Failed Updates

If batch update fails:
1. Error is logged
2. Message IDs are re-queued for retry
3. Next flush attempt

### Backpressure

If accumulator reaches capacity:
- Blocks new enqueues (waits for batch to flush)
- Prevents unbounded memory growth
- Queue size = BatchSize

## Optimization Tips

### For Maximum Throughput
```json
{
  "Publishing": {
    "BatchSize": 20000,
    "FlushIntervalMs": 100
  }
}
```

### For Lowest Latency
```json
{
  "Publishing": {
    "BatchSize": 1000,
    "FlushIntervalMs": 100
  }
}
```

### For Balanced Performance
```json
{
  "Publishing": {
    "BatchSize": 5000,
    "FlushIntervalMs": 500
  }
}
```

## Database Index Strategy

Critical indexes for publish operations:

```sql
-- Find unpublished messages quickly
CREATE INDEX IX_Outbox_Publish ON Outbox(Publish, CreatedAt) 
WHERE Publish = 0;

-- Track production timeline
CREATE INDEX IX_Outbox_ProducedAt ON Outbox(ProducedAt) 
WHERE ProducedAt IS NOT NULL;
```

## Example: Full Publishing Workflow

```csharp
public class PublishingWorkflow
{
    private readonly IOutboxService _outboxService;
    private readonly IKafkaService _kafkaService;
    private readonly IPublishBatchHandler _batchHandler;
    private readonly ILogger<PublishingWorkflow> _logger;

    public async Task PublishMessageAsync(OutboxMessage message, CancellationToken ct)
    {
        try
        {
            // 1. Produce to Kafka
            var startTime = DateTime.UtcNow;
            await _kafkaService.ProduceMessageAsync(message, ct);
            
            // 2. Mark production timestamp
            await _outboxService.MarkMessageAsProducedAsync(
                message.Id, startTime, ct);
            
            // 3. Enqueue for batch publishing
            await _batchHandler.EnqueueForPublishAsync(message.Id, ct);
            
            _logger.LogInformation(
                "Message {MessageId} published: SecType={SecType}, Domain={Domain}",
                message.Id, message.SecType, message.Domain);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish message {MessageId}", message.Id);
            throw;
        }
    }

    public async Task MarkReceivedAsync(OutboxMessage message, CancellationToken ct)
    {
        try
        {
            await _outboxService.MarkMessageAsReceivedAsync(
                message.Id, DateTime.UtcNow, ct);
            
            _logger.LogInformation(
                "Message {MessageId} received on downstream topic", message.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to mark message as received {MessageId}", message.Id);
            throw;
        }
    }
}
```

## Troubleshooting

### Unpublished Messages Growing

1. Check application logs for batch flush errors
2. Verify database connectivity
3. Check index fragmentation
4. Monitor database disk space

### High Latency

1. Increase `BatchSize` for better throughput
2. Decrease `FlushIntervalMs` for faster processing
3. Check database indexes are used (run EXPLAIN PLAN)
4. Monitor lock waits

### Memory Leak

1. Ensure `FlushAsync()` is called (background service does this)
2. Check for exception in batch handler preventing flush
3. Monitor memory growth over time

## Best Practices

1. **Always use batch operations** for multiple messages
2. **Configure appropriate batch size** based on message volume
3. **Monitor unpublished backlog** in production
4. **Archive old messages** (> 3 months) for performance
5. **Set up alerts** for stuck messages (unpublished > 1 hour)
6. **Rebuild indexes** periodically to maintain performance
7. **Test failover** with database connectivity interruptions

## Related Documentation

- [DATABASE_SCHEMA.sql](./DATABASE_SCHEMA.sql) - Complete schema with indexes
- [PRODUCTION_GUIDE.md](./PRODUCTION_GUIDE.md) - Deployment procedures
- [QUICK_REFERENCE.md](./QUICK_REFERENCE.md) - Quick commands
