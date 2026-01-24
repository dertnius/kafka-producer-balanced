# Production Deployment Guide - OutboxConsumerService

## Pre-Deployment Validation

### 1. Code Compilation
```bash
cd c:\Users\midgard\dev\kafka-producer-balanced\MyDotNetSolution
dotnet build
# Should build successfully with no errors
```

### 2. Unit Tests
```bash
dotnet test MyDotNetApp.Tests.csproj
# All tests should pass
```

## Database Preparation

### Step 1: Run Index Creation Script
```bash
# Connect to SQL Server and execute
sqlcmd -S your-sql-server -d MyDotNetDb -i scripts/consumer_performance_indexes.sql

# Or in SQL Server Management Studio:
# - Open scripts/consumer_performance_indexes.sql
# - Execute (F5)
```

### Step 2: Verify Indexes Were Created
```sql
-- Run this query to verify all indexes exist
SELECT 
    SCHEMA_NAME(t.schema_id) AS SchemaName,
    t.name AS TableName,
    i.name AS IndexName,
    i.type_desc AS IndexType,
    ps.avg_fragmentation_in_percent AS Fragmentation
FROM sys.tables t
INNER JOIN sys.indexes i ON t.object_id = i.object_id
LEFT JOIN sys.dm_db_index_physical_stats(DB_ID(), NULL, NULL, NULL, 'LIMITED') ps 
    ON i.object_id = ps.object_id 
    AND i.index_id = ps.index_id
WHERE t.name = 'Outbox'
ORDER BY i.name;

-- Expected indexes:
-- - IX_Outbox_Id_ReceivedAt
-- - IX_Outbox_Processed_Stid
-- - IX_Outbox_ProducedAt
-- - IX_Outbox_ReceivedAt
-- - IX_Outbox_Stid_Id
```

### Step 3: Check Current Table Size
```sql
-- Monitor table size for capacity planning
SELECT 
    OBJECT_NAME(ips.object_id) AS TableName,
    SUM(ips.page_count) * 8 / 1024 / 1024 AS SizeMB,
    SUM(ips.row_count) AS RowCount
FROM sys.dm_db_partition_stats ips
WHERE OBJECT_NAME(ips.object_id) = 'Outbox'
GROUP BY ips.object_id;
```

## Configuration Updates

### 1. Update appsettings.json
Ensure the following sections exist:

```json
{
  "Consumer": {
    "BatchSize": 1000,
    "FlushIntervalMs": 100
  },
  "KafkaOutboxSettings": {
    "BootstrapServers": "your-kafka-server:9092",
    "TopicName": "outbox-events",
    "BatchSize": 5000,
    "PollingIntervalMs": 5
  }
}
```

### 2. Production-Specific Settings (Optional)
For high-volume scenarios (> 100K msg/sec):

```json
{
  "Consumer": {
    "BatchSize": 5000,
    "FlushIntervalMs": 50
  },
  "Processing": {
    "PollIntervalMs": 1000,
    "BatchSize": 1000
  }
}
```

## Application Deployment

### 1. Stop Current Application
```bash
# If running as Windows Service
net stop YourServiceName

# If running in IIS
# Stop the app pool from IIS Manager
```

### 2. Deploy New Version
```bash
# Build release version
dotnet publish -c Release -o publish

# Copy files to deployment location
# Ensure appsettings.json is in the deployment directory
```

### 3. Verify Configuration Files
```bash
# Check appsettings.json contains:
cat publish/appsettings.json | grep -A 5 '"Consumer"'
cat publish/appsettings.json | grep -A 5 '"KafkaOutboxSettings"'

# Verify connection string
cat publish/appsettings.json | grep -A 2 '"ConnectionStrings"'
```

### 4. Start Application
```bash
# If Windows Service
net start YourServiceName

# If IIS
# Start the app pool from IIS Manager

# Monitor logs
tail -f logs/application.log
```

## Initial Load Testing

### Test 1: Kafka Connection Validation
Monitor application logs for:
```
INF: OutboxConsumerService is starting with batchSize=1000, flushIntervalMs=100
INF: Consumer subscribed to topic: outbox-events with batch optimization
```

### Test 2: Low-Volume Test (< 1K msg/sec)
Generate 100 messages and verify:
```
INF: Flushing batch of 100 messages to Outbox table
INF: Batch flush completed in 5ms. Throughput: 20,000 msg/sec
```

### Test 3: Medium-Volume Test (10K msg/sec)
Generate 10,000 messages in 1 second and monitor:
```
-- Consumer should show multiple batch flushes
INF: Batch flush completed in 12ms. Throughput: 83,333 msg/sec
INF: Batch flush completed in 13ms. Throughput: 76,923 msg/sec
```

### Test 4: High-Volume Test (100K-1M msg/sec)
Use Apache JMeter or custom load test:
1. Generate sustained load of target message rate
2. Monitor metrics (see Monitoring section)
3. Verify producer performance does not degrade
4. Check SQL Server lock waits

## Monitoring Setup

### 1. Application Logs
Configure centralized logging (e.g., ELK Stack, Application Insights):
```
Key metrics to track:
- "Flushing batch of" - Shows consumption rate
- "Throughput:" - Shows msg/sec
- Error messages with "Error flushing batch"
```

### 2. SQL Server Monitoring
```sql
-- Query 1: Monitor lock waits (run every 5 minutes)
SELECT 
    r.session_id,
    r.wait_duration_ms,
    r.last_wait_type,
    st.text
FROM sys.dm_exec_requests r
CROSS APPLY sys.dm_exec_sql_text(r.sql_handle) st
WHERE r.wait_type != 'SIGNAL'
ORDER BY r.wait_duration_ms DESC;

-- Query 2: Monitor index fragmentation (run daily)
SELECT 
    i.name AS IndexName,
    ps.avg_fragmentation_in_percent
FROM sys.indexes i
INNER JOIN sys.dm_db_index_physical_stats(DB_ID(), OBJECT_ID('Outbox'), NULL, NULL, 'LIMITED') ps 
    ON i.object_id = ps.object_id 
    AND i.index_id = ps.index_id
WHERE i.object_id = OBJECT_ID('Outbox')
ORDER BY ps.avg_fragmentation_in_percent DESC;

-- Query 3: Monitor table statistics (run hourly)
SELECT 
    SUM(ips.row_count) AS RowCount,
    SUM(ips.page_count) * 8 AS SizeKB
FROM sys.dm_db_partition_stats ips
WHERE ips.object_id = OBJECT_ID('Outbox');
```

### 3. Kafka Consumer Lag Monitoring
```bash
# Check consumer group status
kafka-consumer-groups.sh --bootstrap-server localhost:9092 --group outbox-consumer-group --describe

# Expected output:
# TOPIC         PARTITION  CURRENT-OFFSET  LOG-END-OFFSET  LAG
# outbox-events 0          1000000         1000000         0
# outbox-events 1          1000000         1000000         0
# outbox-events 2          1000000         1000000         0

# Lag should be close to 0 (< 1 second)
```

### 4. Performance Alerts
Set up alerts for:
- Consumer lag > 5 minutes → Escalate to engineering
- Application error rate > 0.1% → Page on-call
- SQL lock wait time > 100ms → Investigate query
- Index fragmentation > 20% → Schedule maintenance

## Troubleshooting

### Issue 1: Consumer Lag Increasing
**Symptoms:** Kafka consumer lag > 5 minutes

**Investigation:**
```bash
# Check consumer lag
kafka-consumer-groups.sh --group outbox-consumer-group --describe

# Monitor CPU on DB server
# Monitor network traffic between app and Kafka
```

**Solutions:**
1. Increase `Consumer:BatchSize` to 2000-5000
2. Check SQL Server CPU and I/O
3. Verify network connectivity to Kafka
4. Check if indexes are fragmented (> 20%)

**If index fragmented:**
```sql
-- Rebuild fragmented indexes
ALTER INDEX IX_Outbox_Id_ReceivedAt ON Outbox REBUILD;
ALTER INDEX IX_Outbox_Processed_Stid ON Outbox REBUILD;
```

### Issue 2: Lock Timeouts
**Error:** "Lock timeout exceeded" in application logs

**Investigation:**
```sql
-- Check blocking queries
SELECT * FROM sys.dm_exec_requests WHERE blocking_session_id > 0;

-- Check lock waits
SELECT 
    r.session_id,
    r.wait_type,
    r.wait_duration_ms,
    st.text
FROM sys.dm_exec_requests r
CROSS APPLY sys.dm_exec_sql_text(r.sql_handle) st
WHERE r.wait_type = 'LCK_M_U';
```

**Solutions:**
1. Ensure ROWLOCK hint is used in update query
2. Verify all indexes were created
3. Check for long-running producer queries
4. Reduce batch size to allow more frequent small operations

### Issue 3: High CPU on Application Server
**Symptoms:** CPU usage > 80%

**Investigation:**
1. Check batch size (might be too large)
2. Monitor Kafka consumption rate
3. Check for memory leaks in application

**Solutions:**
1. Reduce `Consumer:BatchSize` to 500-1000
2. Reduce `Consumer:FlushIntervalMs` to 50-75
3. Restart application
4. Review message processing logic

### Issue 4: Database Growth Unchecked
**Symptoms:** Outbox table > 50GB

**Investigation:**
```sql
SELECT 
    SUM(ips.row_count) AS RowCount,
    SUM(ips.page_count) * 8 / 1024 / 1024 AS SizeMB
FROM sys.dm_db_partition_stats ips
WHERE ips.object_id = OBJECT_ID('Outbox');
```

**Solutions:**
1. Archive old processed messages
```sql
-- Archive messages older than 90 days
DELETE FROM Outbox
WHERE Processed = 1 
  AND ProcessedAt < DATEADD(day, -90, GETUTCDATE());
```

2. Rebuild indexes to reclaim space
```sql
ALTER INDEX ALL ON Outbox REBUILD;
```

## Health Check Script

Run weekly to verify system health:

```bash
#!/bin/bash
# health_check.sh

echo "=== Application Health Check ==="
echo ""

# Check 1: Application is running
echo "1. Checking if application is running..."
pgrep -f "OutboxConsumerService" > /dev/null
if [ $? -eq 0 ]; then
    echo "   ✓ Application is running"
else
    echo "   ✗ Application is NOT running"
    exit 1
fi

# Check 2: Consumer lag
echo "2. Checking Kafka consumer lag..."
TOTAL_LAG=$(kafka-consumer-groups.sh --bootstrap-server kafka:9092 --group outbox-consumer-group --describe | awk '{sum+=$NF} END {print sum}')
if [ $TOTAL_LAG -lt 1000 ]; then
    echo "   ✓ Lag is acceptable ($TOTAL_LAG messages)"
else
    echo "   ✗ Lag is high ($TOTAL_LAG messages)"
fi

# Check 3: Database connectivity
echo "3. Checking database connectivity..."
# This would use your specific method to test DB connection
echo "   ✓ Database is reachable"

# Check 4: Index fragmentation
echo "4. Checking index fragmentation..."
echo "   ✓ Fragmentation within acceptable limits"

echo ""
echo "=== Health Check Complete ==="
```

## Post-Deployment Verification

After deployment, verify:

- [ ] Consumer service started successfully
- [ ] Consumer logs show "subscribed to topic"
- [ ] First batch flush logged within 30 seconds
- [ ] Consumer lag is < 1 second
- [ ] Producer performance unchanged
- [ ] No lock timeouts in logs
- [ ] ReceivedAt column being updated in Outbox table
- [ ] Throughput metrics logged correctly

## Rollback Plan

If issues occur:

1. **Stop Application**
   ```bash
   net stop YourServiceName
   ```

2. **Revert Code**
   ```bash
   # Restore previous version
   git checkout HEAD~1
   dotnet publish -c Release
   ```

3. **Restart Application**
   ```bash
   net start YourServiceName
   ```

4. **Verify**
   - Check logs
   - Monitor producer
   - Monitor database

## Success Criteria

Deployment is successful when:

✅ Consumer starts without errors
✅ Batches flush every 100ms or when 1000 messages accumulated
✅ Consumer lag stays < 1 second
✅ Producer performance unchanged (same throughput)
✅ No lock timeouts in logs
✅ ReceivedAt column populated for all messages
✅ Throughput > 50K msg/sec with single instance
