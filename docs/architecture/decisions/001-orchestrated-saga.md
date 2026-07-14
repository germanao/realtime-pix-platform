# ADR-001: Use an Orchestrated Saga

**Status:** Accepted

## Context

A transfer changes state in two banks that own separate databases. A cross-database ACID transaction would couple services and is not available through Azure Service Bus.

## Decision

Transaction Service is the durable coordinator. It persists an explicit state/version/history and emits debit, credit, or refund commands through its transactional outbox. Bank outcomes drive guarded transitions. Timeouts are persisted recovery decisions, not frontend timers.

## Consequences

- Normal completion and compensation are explainable and replay-safe.
- Temporary inconsistency is expected between bank transactions.
- Manual intervention is an explicit terminal state when compensation cannot finish.
- Operators need alerts and a recovery procedure for unresolved liabilities.
- This is a Saga because each participant commits locally and compensation is a business action, not a database rollback.
