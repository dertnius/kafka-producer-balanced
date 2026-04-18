# OpenSpec Initialization

This folder contains the project OpenSpec baseline for current observable behavior.

## Structure

- `specs/`: Current-state behavior specs by domain.
- `changes/`: Proposed modifications and delta specs.

## Domains Initialized

- `outbox-api`
- `outbox-processing`
- `kafka-publishing`
- `outbox-consumption`

## Next Step

When introducing new behavior, create a folder in `changes/` with:

- `proposal.md`
- `design.md`
- `tasks.md`
- `specs/{domain}/spec.md` (delta with ADDED/MODIFIED/REMOVED)
