-- Check for blocking among consumer instances
-- Run this while consumers are active to see if they block each other

SELECT 
    blocking.session_id AS BlockingSessionId,
    blocked.session_id AS BlockedSessionId,
    blocked.wait_time AS WaitTimeMs,
    blocked.wait_type,
    blocking_sql.text AS BlockingSQL,
    blocked_sql.text AS BlockedSQL
FROM sys.dm_exec_requests AS blocked
INNER JOIN sys.dm_exec_requests AS blocking
    ON blocked.blocking_session_id = blocking.session_id
CROSS APPLY sys.dm_exec_sql_text(blocking.sql_handle) AS blocking_sql
CROSS APPLY sys.dm_exec_sql_text(blocked.sql_handle) AS blocked_sql
WHERE blocked.blocking_session_id > 0
ORDER BY blocked.wait_time DESC;

-- If this returns no rows → No blocking!
-- If it shows rows → Consumers are blocking each other
