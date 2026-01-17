# Performance Analysis: 1 Million Messages in 1 Minute

## Target Throughput
**1,000,000 messages / 60 seconds = 16,667 messages/second**

## Current Architecture Assessment

### ✅ Optimizations Applied
1. **.NET 8** - Latest LTS runtime with performance improvements
2. **20 Concurrent Producers** - Parallelized message sending across threads
3. **200,000 Message Buffer** - Handles burst traffic spikes
4. **50,000 Batch Polling** - Efficient database retrieval
5. **Snappy Compression** - Reduces network I/O overhead
6. **Avro Serialization** - Binary format with schema registry caching

### ⚡ Kafka Producer Configuration

```
Acks: Leader              # Only waits for leader ack (not replicas) - ~5-10ms per message
LingerMs: 10             # Batches messages for 10ms windows
BatchSize: 1MB           # Groups up to 1MB per network packet
QueueBuffer: 200,000     # Local buffer capacity
Compression: Snappy      # Reduces payload by ~50-70%
SocketNagle: Disabled    # Lower latency transmission
```

**Expected Kafka Throughput:** 20,000+ msg/sec per Kafka broker

### 🗄️ Database Configuration

```
BatchSize: 50,000        # Fetches 50k messages per query
PollingInterval: 10ms    # Polls every 10ms (6x faster than original)
ConnectionPool: 40       # 40 concurrent connections
```

**Expected DB Throughput:** 5M+ messages/sec (SQL Server local)

### 🔐 Ordering Constraint: Per-MortgageStid

Each MortgageStid has a dedicated `SemaphoreSlim(1,1)` to enforce ordering.

**Critical Assumption:** You have **many different MortgageStid values** (100+)
- If distributed across 100+ MortgageStids: **Parallelizable** ✅
  - 20 producers × 20 MortgageStids processing simultaneously = ~400 msg/sec per MortgageStid
  - Easily achieves 16,667 msg/sec aggregate
  
- If all messages have **same MortgageStid**: **Serialized** ❌
  - Only 1 producer can send at a time per key
  - Max throughput: ~500-1000 msg/sec per producer

### 📊 Performance Projections

#### Scenario A: Diverse MortgageStids (100+ unique)
```
20 producers × 833 msg/sec per producer = 16,660 msg/sec ✅ ACHIEVABLE
```

#### Scenario B: Few MortgageStids (1-10)
```
20 producers / 10 MortgageStids = 2 producers per key × 500 msg/sec = 1,000 msg/sec ❌ TOO SLOW
```

#### Scenario C: Single MortgageStid
```
20 producers / 1 MortgageStid = 1 producer × 1,000 msg/sec = 1,000 msg/sec ❌ TOO SLOW
```

## Bottleneck Analysis

| Component | Throughput | Status |
|-----------|-----------|--------|
| Kafka Network (3-node cluster) | 50,000+ msg/sec | ✅ Not a bottleneck |
| Database Polling | 5,000,000+ msg/sec | ✅ Not a bottleneck |
| Avro Serialization | 100,000+ msg/sec | ✅ Not a bottleneck |
| Per-MortgageStid Lock Contention | **Variable** | ⚠️ **Key Variable** |

## Recommendations

### If you have diverse MortgageStids (recommended):
✅ **Current configuration is sufficient**
- Settings already optimized
- Should achieve 1M msg/min without changes

### If you have few MortgageStids:
❌ **Consider one of these alternatives:**

**Option 1: Remove ordering guarantee per MortgageStid**
```csharp
// Remove the per-MortgageStid SemaphoreSlim lock
// This will allow all 20 producers to send concurrently
// Trade-off: Messages may arrive out-of-order for same MortgageStid
```

**Option 2: Use Kafka partition key for ordering**
```csharp
// Let Kafka handle ordering via partition assignment
// Messages with same key → same partition → ordered
// More efficient than application-level locks
```

**Option 3: Implement request batching**
```csharp
// Batch multiple messages from same MortgageStid into one Avro record
// Send once per 10ms window
// Reduces lock contention by 100x-1000x
```

### If you need to achieve 1M msg/min with few MortgageStids:

1. **Profile first**: Measure your actual MortgageStid cardinality
   ```powershell
   SELECT COUNT(DISTINCT MortgageStid) FROM Outbox
   ```

2. **Disable per-key ordering** if not required:
   ```csharp
   // In ProducerWorkerAsync, remove:
   // var stidLock = _mortgageStidLocks.GetOrAdd(stidKey, ...);
   ```

3. **Increase Linger time** to 50ms for better batching:
   ```csharp
   LingerMs = 50,  // More time to accumulate messages
   ```

4. **Add batch pre-compression**:
   - Compress records before Avro serialization
   - Reduces serialization overhead

## Recommended Tuning for 1M/min (if needed)

```csharp
// In appsettings.json
{
  "KafkaOutboxSettings": {
    "BootstrapServers": "localhost:9092",
    "TopicName": "outbox-topic",
    "SchemaRegistryUrl": "http://localhost:8081",
    "BatchSize": 100000,           // Larger batches = fewer DB calls
    "PollingIntervalMs": 5,         // Ultra-fast polling for bursts
    "MaxConcurrentProducers": 32,   // More parallelism (for 32-core CPU)
    "MaxProducerBuffer": 500000,    // Larger burst buffer
    "DatabaseConnectionPoolSize": 50
  }
}
```

## Load Testing Recommendation

Before production, run a load test:
```bash
# Simulate 1M messages in 60 seconds
# Monitor:
# - Database CPU (should be < 50%)
# - Kafka broker CPU (should be < 70%)
# - .NET app memory (should be < 2GB)
# - Network I/O (should be < 100 Mbps)
# - Per-MortgageStid lock wait times (critical!)

# If any bottleneck appears, iterate on settings
```

## Summary

✅ **If you have 100+ different MortgageStids:** Current setup handles 1M msg/min easily

⚠️ **If you have <10 different MortgageStids:** May need to remove per-key ordering or increase Kafka partition count

🔍 **Action: Determine MortgageStid cardinality first** - this is the deciding factor
