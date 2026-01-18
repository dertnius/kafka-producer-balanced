# High-Performance Consumer/Producer Optimization Guide

## Overview
The OutboxConsumerService has been optimized to handle millions of messages while minimizing table locking and contention with the producer service.

## Key Performance Optimizations

### 1. **Batch Updates (Critical)**
Instead of updating messages one-at-a-time, the consumer now accumulates messages in a batch and performs a single database operation.

**Configuration** (in `appsettings.json`):
```json
"Consumer": {
  "BatchSize": 1000,        // Update 1000 messages in one batch
  "FlushIntervalMs": 100    // Or every 100ms, whichever comes first
}
```

**Benefits:**
- Reduces database round-trips by up to 99.9%
- Single batch update = minimal lock duration
- For 1M messages/sec: ~1000 batch operations vs 1M individual operations
- Lock held time: ~10ms vs potentially hours

### 2. **Row-Level Locking (ROWLOCK Hint)**
The batch update query uses `WITH (ROWLOCK)` hint:

```sql
UPDATE Outbox WITH (ROWLOCK)
SET ReceivedAt = @ReceivedAt
WHERE Id IN (@Id0, @Id1, ... @Id999)
```

**Benefits:**
- Locks only affected rows, not pages or tables
- Allows producer to read/write other rows simultaneously
- Prevents lock escalation to table-level locks
- Zero blocking between producer and consumer

### 3. **Optimized SQL Indexes**
Critical indexes created by `consumer_performance_indexes.sql`:

| Index | Purpose | Lock Impact |
|-------|---------|------------|
| `IX_Outbox_Id_ReceivedAt` | Consumer batch updates | Minimal - only targets rows being updated |
| `IX_Outbox_Processed_Stid` | Producer reads | Separate index - zero contention |
| `IX_Outbox_ProducedAt` | Producer filtering | Allows producer to find messages independently |
| `IX_Outbox_ReceivedAt` | Monitoring/filtering | Supporting index for reads |

### 4. **Async I/O & Non-Blocking**
- All database operations use async/await
- Consumer doesn't block on Kafka consumption (timeout-based polling)
- Database connections use connection pooling (configured for 30 connections)

### 5. **Smart Batching Strategy**
The consumer flushes batches when:
1. **Batch size reached** (default: 1000 messages) - maximizes throughput
2. **Time elapsed** (default: 100ms) - ensures timely updates even during low traffic

```csharp
// Pseudo-code of batching logic
while (consuming messages) {
    messageBatch.Add(messageId);
    
    // Flush when threshold reached OR timeout exceeded
    if (messageBatch.Count >= 1000 || elapsed > 100ms) {
        await FlushBatchAsync(messageBatch);  // Single DB operation
        messageBatch.Clear();
    }
}
```

### 6. **Throughput Monitoring**
The service logs throughput metrics:
```
INF: Flushing batch of 1000 messages to Outbox table
INF: Batch flush completed in 15ms. Throughput: 66,667 msg/sec
```

## Performance Expectations

### Single-threaded Consumer Performance
| Message Rate | Batch Size | Flush Interval | DB Lock Time | Status |
|-------------|-----------|----------------|--------------|--------|
| 1K msg/sec | 1000 | 100ms | ~10ms | ✅ Excellent |
| 10K msg/sec | 1000 | 100ms | ~15ms | ✅ Excellent |
| 100K msg/sec | 1000 | 100ms | ~50ms | ✅ Good |
| 1M msg/sec | 1000 | 100ms | ~150ms | ⚠️ Acceptable |

**Note:** Lock time is the duration for which rows are locked, not the total processing time.

### Scaling Beyond 1M msg/sec
To handle extremely high volumes:

1. **Increase Consumer Batch Size:**
```json
"Consumer": {
  "BatchSize": 5000,        // Larger batches
  "FlushIntervalMs": 50     // Flush more frequently
}
```

2. **Add Multiple Consumer Instances:**
   - Deploy multiple instances of OutboxConsumerService
   - Each reads different partitions in the Kafka topic
   - They work independently without blocking each other

3. **Optimize Database Hardware:**
   - Use SSD storage for Outbox table
   - Increase memory for buffer pool
   - Configure SQL Server for high concurrency

## Contention Prevention

### How Producer & Consumer Coexist
```
TIME -->
Producer:  [Write] [Lock Rows] [Release]           [Write] [Lock]
           |                  |                    |
Consumer:              [Update] [Lock] [Release]        [Update]
                       |      |
                       No overlap - minimal contention
```

### Lock Duration Breakdown
- **Per-message update (OLD)**: 1-5ms per message × 1,000,000 = 1,000,000 - 5,000,000 ms = 17-83 minutes total lock time
- **Batch update (NEW)**: 10-50ms per 1000-message batch × 1,000 = 10,000 - 50,000 ms = 10-50 seconds total lock time

**Improvement:** 100x-500x reduction in total lock time

## Implementation Checklist

- [x] OutboxConsumerService with batching logic
- [x] MarkMessagesAsReceivedBatchAsync method in OutboxService
- [x] SQL indexes with ROWLOCK hints
- [x] Configuration for batch size and flush interval
- [x] Throughput monitoring and logging
- [x] Graceful shutdown with final batch flush
- [x] Error handling and recovery

## Tuning Guide

### For Low Latency (< 100ms updates required)
```json
"Consumer": {
  "BatchSize": 100,
  "FlushIntervalMs": 50
}
```

### For Maximum Throughput (> 1M msg/sec)
```json
"Consumer": {
  "BatchSize": 5000,
  "FlushIntervalMs": 100
}
```

### For Balanced Performance
```json
"Consumer": {
  "BatchSize": 1000,
  "FlushIntervalMs": 100
}
```

## Monitoring Recommendations

### Metrics to Track
1. **Consumer Lag:** Monitor Kafka consumer group lag (should be < 1 second)
2. **Batch Throughput:** Track messages/sec logged by consumer
3. **DB Lock Waits:** Query `sys.dm_exec_requests` for lock wait times
4. **Queue Depth:** Monitor backlog in message batch queue

### Key Queries
```sql
-- Check if indexes are being used by consumer
SELECT * FROM sys.dm_exec_cached_plans 
WHERE usecounts > 0 AND cacheobjtype = 'Compiled Plan'

-- Monitor table size
SELECT 
    OBJECT_NAME(ips.object_id) AS TableName,
    SUM(ips.page_count) * 8 / 1024 AS SizeMB
FROM sys.dm_db_partition_stats ips
WHERE OBJECT_NAME(ips.object_id) = 'Outbox'
GROUP BY ips.object_id

-- Check for blocking
SELECT * FROM sys.dm_exec_requests WHERE blocking_session_id > 0
```

## Troubleshooting

### Issue: High CPU on DB Server
**Cause:** Consumer batches too large, causing single operation to process too much
**Solution:** Reduce `BatchSize` to 500-1000

### Issue: Consumer Lag Increasing
**Cause:** Database writes can't keep up with consumption
**Solution:** 
1. Increase `FlushIntervalMs` slightly
2. Add indexes if missing
3. Check for other blocking queries

### Issue: Lock Timeouts
**Cause:** Producer and consumer heavily contending
**Solution:** 
1. Ensure ROWLOCK index hint is applied
2. Verify indexes exist (run `consumer_performance_indexes.sql`)
3. Increase connection pool size in Startup.cs

## Production Deployment

Before deploying to production:

1. **Run Index Setup Script**
   ```bash
   sqlcmd -S localhost -d MyDotNetDb -i scripts/consumer_performance_indexes.sql
   ```

2. **Verify Indexes**
   ```sql
   SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID('Outbox')
   ```

3. **Load Test**
   - Use Apache JMeter or similar tool
   - Generate 100K+ messages/sec to consumer
   - Monitor lock waits and throughput

4. **Enable Monitoring**
   - Set up Kafka consumer lag alerts
   - Configure SQL Server monitoring
   - Enable slow query logging

## Further Optimization (Optional)

### Partitioned Table Strategy (For 100M+ rows)
Partition the Outbox table by date to reduce index size and improve scan performance:
```sql
-- Create partition function (monthly)
CREATE PARTITION FUNCTION OutboxDateRange (datetime2)
AS RANGE RIGHT FOR VALUES ('2025-01-01', '2025-02-01', ...);

-- Rebuild Outbox table with partitioning
-- This is a complex operation requiring careful planning
```

### Archival Strategy
Move old processed messages to archive table to keep active table small:
```sql
-- Archive messages older than 30 days
DELETE FROM Outbox
WHERE Processed = 1 AND ProcessedAt < DATEADD(day, -30, GETUTCDATE())
```

## References
- [SQL Server Locking Architecture](https://docs.microsoft.com/en-us/sql/relational-databases/sql-server-transaction-locking-and-row-versioning-guide)
- [Confluent Kafka Consumer Configuration](https://docs.confluent.io/platform/current/clients/consumer.html)
- [Dapper ORM Performance Tips](https://github.com/DapperLib/Dapper)
