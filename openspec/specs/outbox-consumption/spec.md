# Outbox Consumption Specification

## Purpose
Defines how Kafka consumer instances ingest messages and update outbox reception status in high-throughput batches.

## Requirements

### Requirement: Parallel Consumer Instances
The system SHALL support multiple consumer background instances in the same consumer group.

#### Scenario: Multiple consumers are configured
- GIVEN consumer instance count is greater than one
- WHEN the application host starts
- THEN multiple consumer services are started
- AND Kafka partition assignment is distributed by consumer group membership

### Requirement: Batch Flush and Commit
The system MUST flush consumed message identifiers in batches and commit offsets after successful persistence.

#### Scenario: Flush on batch size threshold
- GIVEN consumed messages are accumulated
- WHEN batch size reaches configured threshold
- THEN the system writes reception updates in batch
- AND commits offsets after successful write

#### Scenario: Flush on interval threshold
- GIVEN consumed messages are accumulated below batch size
- WHEN flush interval threshold is reached
- THEN the system writes reception updates in batch
- AND commits offsets after successful write

### Requirement: Shutdown Data Safety
The system SHALL flush any remaining buffered consumed messages before service stop completes.

#### Scenario: Graceful shutdown with buffered messages
- GIVEN shutdown begins with unflushed buffered messages
- WHEN the consumer service exits main loop
- THEN remaining messages are flushed
- AND final offsets are committed after successful flush

### Requirement: Error Recovery Loop
The system SHOULD continue consumption after transient errors.

#### Scenario: Consumption error with buffered messages
- GIVEN an error occurs during consume loop
- WHEN buffered messages exist
- THEN the system attempts to flush buffered messages
- AND resumes loop after a short backoff unless cancellation is requested
