-- =====================================================
-- Performance Indexes for Consumer/Producer Separation
-- =====================================================
-- This script creates indexes to minimize contention between
-- the OutboxConsumerService (updating ReceivedAt) and producer
-- while supporting millions of messages with minimal blocking

-- Index 1: Support consumer batch updates on ReceivedAt
-- This allows the consumer to efficiently update multiple messages
-- without blocking producer reads
CREATE NONCLUSTERED INDEX IX_Outbox_Id_ReceivedAt
ON Outbox(Id)
INCLUDE (ReceivedAt, Processed)
WHERE ReceivedAt IS NULL
WITH (FILLFACTOR = 80);  -- Leave space for updates to minimize page splits

-- Index 2: Support producer operations filtering by Processed status
-- This allows producer to query unprocessed messages without blocking consumer
CREATE NONCLUSTERED INDEX IX_Outbox_Processed_Stid
ON Outbox(Processed, Stid)
INCLUDE (Id, Code, Rank, Retry, ErrorCode, ProducedAt)
WITH (FILLFACTOR = 80);

-- Index 3: Support efficient filtering of messages pending production
-- Allows producer to find messages that haven't been produced yet
CREATE NONCLUSTERED INDEX IX_Outbox_ProducedAt
ON Outbox(ProducedAt)
WHERE ProducedAt IS NULL
INCLUDE (Id, Stid, Processed)
WITH (FILLFACTOR = 80);

-- Index 4: Support queries on ReceivedAt for consumer status monitoring
CREATE NONCLUSTERED INDEX IX_Outbox_ReceivedAt
ON Outbox(ReceivedAt)
INCLUDE (Id, Processed, ProducedAt)
WITH (FILLFACTOR = 80);

-- Index 5: Support Stid-based queries with low contention
-- Allows batch operations on specific security IDs
CREATE NONCLUSTERED INDEX IX_Outbox_Stid_Id
ON Outbox(Stid, Id)
INCLUDE (Processed, ProducedAt, ReceivedAt)
WITH (FILLFACTOR = 80);

-- =====================================================
-- STATISTICS UPDATES
-- =====================================================
-- Enable automatic statistics update for high-traffic tables
ALTER DATABASE MyDotNetDb SET AUTO_UPDATE_STATISTICS ON;
ALTER DATABASE MyDotNetDb SET AUTO_UPDATE_STATISTICS_ASYNC ON;

-- =====================================================
-- TABLE CONFIGURATION
-- =====================================================
-- Set page lock escalation to minimize full table locks
-- This is critical when both producer and consumer are active
ALTER TABLE Outbox SET (LOCK_ESCALATION = AUTO);

-- =====================================================
-- VERIFY INDEXES WERE CREATED
-- =====================================================
SELECT 
    name,
    type_desc,
    is_primary_key,
    is_unique
FROM sys.indexes
WHERE object_id = OBJECT_ID('Outbox')
ORDER BY name;

-- =====================================================
-- MONITOR INDEX FRAGMENTATION (Run periodically)
-- =====================================================
-- When fragmentation exceeds 10%, rebuild the index
-- When fragmentation is between 5-10%, reorganize
-- Query to check fragmentation:
/*
SELECT 
    i.name AS IndexName,
    ps.avg_fragmentation_in_percent,
    ps.page_count
FROM sys.indexes i
INNER JOIN sys.dm_db_index_physical_stats(DB_ID(), OBJECT_ID('Outbox'), NULL, NULL, 'LIMITED') ps
    ON i.object_id = ps.object_id
    AND i.index_id = ps.index_id
WHERE database_id = DB_ID()
    AND ps.avg_fragmentation_in_percent > 0
ORDER BY ps.avg_fragmentation_in_percent DESC;
*/
