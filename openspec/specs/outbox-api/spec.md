# Outbox API Specification

## Purpose
Defines externally observable HTTP behavior for creating, querying, and manually triggering outbox processing.

## Requirements

### Requirement: Manual Trigger Endpoint
The system SHALL expose an API endpoint that allows operators to manually trigger outbox polling.

#### Scenario: Trigger processing when pending messages exist
- GIVEN an operator sends `POST /api/outbox/trigger`
- WHEN unprocessed messages are available in the outbox source
- THEN the API responds with success
- AND the response includes `messagesAdded` greater than zero

#### Scenario: Trigger processing when no pending messages exist
- GIVEN an operator sends `POST /api/outbox/trigger`
- WHEN no unprocessed messages are available
- THEN the API responds with success
- AND the response message indicates no messages were available

### Requirement: Outbox Status Endpoints
The system MUST expose read endpoints for operational visibility.

#### Scenario: Retrieve processor statistics
- GIVEN a client requests `GET /api/outbox/stats`
- WHEN the outbox processor is running
- THEN the API returns current processor statistics

#### Scenario: Retrieve pending messages
- GIVEN a client requests `GET /api/outbox/pending?batchSize=100`
- WHEN the outbox table contains unpublished messages
- THEN the API returns a list of pending messages
- AND the response includes a total count

### Requirement: Outbox Message Creation
The system MUST allow creating new outbox messages via API.

#### Scenario: Create valid outbox message
- GIVEN a valid outbox message payload
- WHEN a client sends `POST /api/outbox/messages`
- THEN the API creates the message in the outbox store
- AND returns HTTP 201 with the created message identifier

#### Scenario: Reject invalid payload
- GIVEN an invalid outbox message payload
- WHEN a client sends `POST /api/outbox/messages`
- THEN the API returns HTTP 400 with validation details
