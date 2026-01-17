-- Create the Outbox table
CREATE TABLE Outbox (
    Id BIGINT IDENTITY(1,1) PRIMARY KEY, -- Unique identifier for each message
    Stid VARCHAR(56) NOT NULL,          -- Identifier for the message group
    Code VARCHAR(255) NULL,             -- Code associated with the message
    Rank INT NOT NULL,                  -- Rank for ordering messages
    Processed TINYINT NOT NULL DEFAULT 0, -- Indicates if the message has been processed
    Retry INT NOT NULL DEFAULT 0,       -- Retry count for failed messages
    ErrorCode VARCHAR(50) NULL,         -- Error code for failed messages
    ReceivedAt DATETIME NULL,           -- Timestamp when the message was acknowledged
    ProducerAt DATETIME NULL            -- Timestamp when the message was sent to Kafka
);

-- Create indexes for the Outbox table
CREATE NONCLUSTERED INDEX IX_Outbox_Processed_Retry_ErrorCode
ON Outbox (Processed, Retry, ErrorCode);

CREATE NONCLUSTERED INDEX IX_Outbox_Stid
ON Outbox (Stid);

CREATE NONCLUSTERED INDEX IX_Outbox_Id
ON Outbox (Id);

CREATE NONCLUSTERED INDEX IX_Outbox_Stid_Id
ON Outbox (Stid, Id);

-- Create the SomeTable table
CREATE TABLE SomeTable (
    Id BIGINT PRIMARY KEY,              -- Unique identifier
    Processed TINYINT NOT NULL DEFAULT 0 -- Indicates if the record has been processed
);

-- Create indexes for SomeTable
CREATE NONCLUSTERED INDEX IX_SomeTable_Processed
ON SomeTable (Processed);

-- Create the OutboxArchive table
CREATE TABLE OutboxArchive (
    Id BIGINT PRIMARY KEY,              -- Unique identifier
    Stid VARCHAR(56) NOT NULL,          -- Identifier for the message group
    Code VARCHAR(255) NULL,             -- Code associated with the message
    Rank INT NOT NULL,                  -- Rank for ordering messages
    Processed TINYINT NOT NULL,         -- Indicates if the message has been processed
    Retry INT NOT NULL,                 -- Retry count for failed messages
    ErrorCode VARCHAR(50) NULL,         -- Error code for failed messages
    ReceivedAt DATETIME NULL,           -- Timestamp when the message was acknowledged
    ProducerAt DATETIME NULL            -- Timestamp when the message was sent to Kafka
);

-- Create a partition function for the Outbox table
CREATE PARTITION FUNCTION StidPartitionFunction (BIGINT)
AS RANGE LEFT FOR VALUES (100000, 200000, 300000, 400000, 500000, 600000, 700000, 800000, 900000);

-- Create a partition scheme for the Outbox table
CREATE PARTITION SCHEME StidPartitionScheme
AS PARTITION StidPartitionFunction
ALL TO ([PRIMARY]);

-- Create the partitioned Outbox table
CREATE TABLE OutboxPartitioned (
    Id BIGINT NOT NULL PRIMARY KEY,
    Stid VARCHAR(56) NOT NULL,
    Code VARCHAR(255),
    Rank INT,
    Processed TINYINT,
    Retry INT,
    ErrorCode VARCHAR(255),
    ReceivedAt DATETIME,
    ProducerAt DATETIME
)
ON StidPartitionScheme (CAST(ABS(CHECKSUM(Stid)) AS BIGINT) % 100000);

-- Create indexes for the partitioned Outbox table
CREATE NONCLUSTERED INDEX IX_OutboxPartitioned_Processed_Retry_ErrorCode
ON OutboxPartitioned (Processed, Retry, ErrorCode)
ON StidPartitionScheme (CAST(ABS(CHECKSUM(Stid)) AS BIGINT) % 100000);

CREATE NONCLUSTERED INDEX IX_OutboxPartitioned_Stid
ON OutboxPartitioned (Stid)
ON StidPartitionScheme (CAST(ABS(CHECKSUM(Stid)) AS BIGINT) % 100000);

CREATE NONCLUSTERED INDEX IX_OutboxPartitioned_Id
ON OutboxPartitioned (Id)
ON StidPartitionScheme (CAST(ABS(CHECKSUM(Stid)) AS BIGINT) % 100000);

-- Create a stored procedure to archive processed messages
CREATE PROCEDURE ArchiveProcessedMessages
AS
BEGIN
    SET NOCOUNT ON;

    -- Move processed messages to the archive table
    INSERT INTO OutboxArchive (Id, Stid, Code, Rank, Processed, Retry, ErrorCode, ReceivedAt, ProducerAt)
    SELECT Id, Stid, Code, Rank, Processed, Retry, ErrorCode, ReceivedAt, ProducerAt
    FROM Outbox
    WHERE Processed = 1;

    -- Delete processed messages from the Outbox table
    DELETE FROM Outbox
    WHERE Processed = 1;
END;

-- Query to monitor partition sizes
SELECT
    p.partition_number,
    COUNT(*) AS RowCount,
    SUM(a.total_pages) * 8 AS PartitionSizeKB
FROM sys.partitions p
JOIN sys.allocation_units a ON p.partition_id = a.container_id
WHERE p.object_id = OBJECT_ID('OutboxPartitioned')
GROUP BY p.partition_number;

-- Query to monitor index usage
SELECT
    i.name AS IndexName,
    i.type_desc AS IndexType,
    dm_ius.user_seeks AS UserSeeks,
    dm_ius.user_scans AS UserScans,
    dm_ius.user_lookups AS UserLookups,
    dm_ius.user_updates AS UserUpdates
FROM sys.indexes i
JOIN sys.dm_db_index_usage_stats dm_ius
    ON i.object_id = dm_ius.object_id AND i.index_id = dm_ius.index_id
WHERE i.object_id = OBJECT_ID('Outbox');