-- Outbox Table Schema with Publishing Features
-- Optimized for high-performance message processing and publishing

-- Initial table creation (if starting from scratch)
CREATE TABLE Outbox (
    -- Primary message identifier
    Id BIGINT PRIMARY KEY IDENTITY(1,1),
    
    -- Standard outbox pattern fields
    AggregateId NVARCHAR(255),
    EventType NVARCHAR(255),
    Payload NVARCHAR(MAX) NOT NULL,
    Processed BIT DEFAULT 0,
    CreatedAt DATETIME2 DEFAULT GETUTCDATE(),
    ProcessedAt DATETIME2 NULL,
    
    -- Legacy compatibility fields
    Stid NVARCHAR(255),
    Code NVARCHAR(255),
    Rank INT DEFAULT 0,
    
    -- Publishing tracking fields
    SecType NVARCHAR(2),           -- Security type (2 chars)
    Domain NVARCHAR(26),           -- Domain (26 chars)
    MortgageStid NVARCHAR(56),     -- Mortgage STID (56 chars)
    SecPoolCode NVARCHAR(4),       -- Security pool code (4 chars)
    Publish BIT DEFAULT 0,         -- Published to primary topic
    ProducedAt DATETIME2 NULL,     -- When produced to topic
    ReceivedAt DATETIME2 NULL,     -- When received from other topic
    
    -- Error tracking
    Retry INT DEFAULT 0,
    ErrorCode NVARCHAR(100)
);

-- Performance-critical indexes
-- Index for finding unprocessed messages
CREATE INDEX IX_Outbox_Processed ON Outbox(Processed, CreatedAt)
    WHERE Processed = 0;

-- Index for finding unpublished messages
CREATE INDEX IX_Outbox_Publish ON Outbox(Publish, CreatedAt)
    WHERE Publish = 0;

-- Index for STID-based grouping
CREATE INDEX IX_Outbox_Stid ON Outbox(Stid, Processed);

-- Index for domain-based queries
CREATE INDEX IX_Outbox_Domain ON Outbox(Domain, Processed)
    WHERE Processed = 0;

-- Index for finding messages in error state
CREATE INDEX IX_Outbox_Retry ON Outbox(Retry, ErrorCode)
    WHERE Retry > 0 AND ErrorCode IS NOT NULL;

-- Index for timestamp-based archival
CREATE INDEX IX_Outbox_CreatedAt ON Outbox(CreatedAt)
    WHERE Processed = 0;

-- ============================================================================
-- Migration Scripts (if adding to existing table)
-- ============================================================================

-- Step 1: Add new columns if they don't exist
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Outbox' AND COLUMN_NAME = 'SecType')
    ALTER TABLE Outbox ADD SecType NVARCHAR(2);

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Outbox' AND COLUMN_NAME = 'Domain')
    ALTER TABLE Outbox ADD Domain NVARCHAR(26);

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Outbox' AND COLUMN_NAME = 'MortgageStid')
    ALTER TABLE Outbox ADD MortgageStid NVARCHAR(56);

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Outbox' AND COLUMN_NAME = 'SecPoolCode')
    ALTER TABLE Outbox ADD SecPoolCode NVARCHAR(4);

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Outbox' AND COLUMN_NAME = 'Publish')
    ALTER TABLE Outbox ADD Publish BIT DEFAULT 0;

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Outbox' AND COLUMN_NAME = 'ProducedAt')
    ALTER TABLE Outbox ADD ProducedAt DATETIME2 NULL;

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Outbox' AND COLUMN_NAME = 'ReceivedAt')
    ALTER TABLE Outbox ADD ReceivedAt DATETIME2 NULL;

-- Step 2: Create indexes if they don't exist
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Outbox_Publish')
    CREATE INDEX IX_Outbox_Publish ON Outbox(Publish, CreatedAt) WHERE Publish = 0;

-- ============================================================================
-- Useful Queries for Monitoring
-- ============================================================================

-- Find all unpublished messages
SELECT Id, Rank, Domain, MortgageStid, SecPoolCode, CreatedAt
FROM Outbox
WHERE Publish = 0
ORDER BY CreatedAt ASC;

-- Count messages by publish status
SELECT 
    Publish,
    COUNT(*) as MessageCount,
    MIN(CreatedAt) as OldestMessage,
    MAX(CreatedAt) as NewestMessage
FROM Outbox
GROUP BY Publish;

-- Find messages stuck in processing
SELECT Id, Rank, Domain, ErrorCode, CreatedAt
FROM Outbox
WHERE Processed = 0 AND Retry > 2
ORDER BY CreatedAt DESC;

-- Count by domain
SELECT Domain, COUNT(*) as MessageCount, COUNT(CASE WHEN Publish = 0 THEN 1 END) as UnpublishedCount
FROM Outbox
WHERE CreatedAt > DATEADD(HOUR, -24, GETUTCDATE())
GROUP BY Domain
ORDER BY MessageCount DESC;

-- Find received messages not yet marked
SELECT Id, Rank, Domain, ReceivedAt, CreatedAt
FROM Outbox
WHERE ReceivedAt IS NOT NULL AND Publish = 0
ORDER BY ReceivedAt DESC;

-- Performance: Messages published per minute (last hour)
SELECT 
    DATEPART(MINUTE, ProducedAt) as Minute,
    COUNT(*) as PublishedCount
FROM Outbox
WHERE ProducedAt > DATEADD(HOUR, -1, GETUTCDATE())
GROUP BY DATEPART(MINUTE, ProducedAt)
ORDER BY Minute DESC;

-- Check table fragmentation (run periodically)
SELECT 
    OBJECT_NAME(ps.object_id) as TableName,
    i.name as IndexName,
    ps.avg_fragmentation_in_percent,
    ps.page_count
FROM sys.dm_db_index_physical_stats(DB_ID(), OBJECT_ID('Outbox'), NULL, NULL, 'LIMITED') ps
INNER JOIN sys.indexes i ON ps.object_id = i.object_id 
    AND ps.index_id = i.index_id
WHERE ps.avg_fragmentation_in_percent > 10
ORDER BY ps.avg_fragmentation_in_percent DESC;

-- ============================================================================
-- Maintenance Scripts
-- ============================================================================

-- Rebuild fragmented indexes (run as maintenance job)
ALTER INDEX IX_Outbox_Publish ON Outbox REBUILD;
ALTER INDEX IX_Outbox_Processed ON Outbox REBUILD;
ALTER INDEX IX_Outbox_CreatedAt ON Outbox REBUILD;

-- Archive old processed messages (adjust date as needed)
DELETE FROM Outbox
WHERE Processed = 1 
  AND Publish = 1 
  AND CreatedAt < DATEADD(MONTH, -3, GETUTCDATE());

-- Update statistics for query optimizer
UPDATE STATISTICS Outbox;

-- ============================================================================
-- Performance Tuning Notes
-- ============================================================================

/*
Key Performance Considerations:

1. Batch Updates:
   - Use parameterized IN clauses for batch publishing
   - Batch size of 5000 is optimal for most scenarios
   - Reduces network round trips significantly

2. Index Strategy:
   - Filtered indexes on common WHERE conditions
   - Covering indexes for frequently accessed columns
   - Monitor fragmentation and rebuild as needed

3. Partition Strategy (Optional for large tables):
   - Partition by CreatedAt for archival and performance
   - Separate old data from hot data
   - Improves index locality

4. Lock Strategy:
   - Avoid row locks during publishing
   - Use NOLOCK hints for read-heavy queries
   - Batch updates minimize lock duration

5. Query Optimization:
   - Always include CreatedAt or Processed in WHERE clause
   - Use TOP with ORDER BY for pagination
   - Avoid SELECT * queries

6. Monitoring:
   - Track unpublished message growth
   - Monitor batch update performance
   - Alert on stuck messages (ProcessedAt IS NULL for > 1 hour)
*/
