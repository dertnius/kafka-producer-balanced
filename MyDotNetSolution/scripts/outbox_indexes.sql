-- Run this script ONCE on your database to optimize the Outbox table
-- for both producer (reads unpublished) and consumer (reads published)

USE YourDatabaseName;  -- Change to your actual database name

-- Index 1: Producer query - "Get messages to publish"
-- Query: SELECT * FROM Outbox WHERE Publish = 0 AND Processed = 1 ORDER BY MortgageStid, Id
CREATE NONCLUSTERED INDEX IX_Outbox_Publish_Processed 
ON dbo.Outbox (Publish, Processed, Id)
INCLUDE (AggregateId, EventType, Rank, SecType, Domain, MortgageStid, SecPoolCode, ProducedAt, ReceivedAt, CreatedAt, ProcessedAt)
WHERE Publish = 0;

-- Index 2: Consumer query - "Get messages to consume"  
-- Query: SELECT * FROM Outbox WHERE Publish = 1 AND Processed = 0 ORDER BY Id
CREATE NONCLUSTERED INDEX IX_Outbox_Processed_Publish 
ON dbo.Outbox (Processed, Publish, Id)
INCLUDE (AggregateId, EventType, Rank, SecType, Domain, MortgageStid, SecPoolCode, ProducedAt, ReceivedAt, CreatedAt, ProcessedAt)
WHERE Processed = 0;

-- Index 3: Update performance - faster UPDATEs on Publish column
CREATE NONCLUSTERED INDEX IX_Outbox_Id_Publish 
ON dbo.Outbox (Id)
INCLUDE (Publish, ProducedAt);

-- Recommendations:
-- 1. Run UPDATE STATISTICS dbo.Outbox after creating indexes
-- 2. Monitor index fragmentation: SELECT * FROM sys.dm_db_index_physical_stats(DB_ID(), OBJECT_ID('Outbox'), NULL, NULL, 'LIMITED')
-- 3. Rebuild if > 30% fragmented: ALTER INDEX IndexName ON dbo.Outbox REBUILD;
-- 4. Reorganize if 10-30% fragmented: ALTER INDEX IndexName ON dbo.Outbox REORGANIZE;
-- 5. Consider partitioning by CreatedAt date if Outbox grows > 1GB
