# ADR-004: Use Transactional Outbox and Durable Inbox

**Status:** Accepted

## Context

Publishing after a database commit can lose a message; publishing before commit can expose state that later rolls back. Brokers can redeliver after lost acknowledgements.

## Decision

Write outgoing envelopes in the owning EF transaction. Background dispatchers claim pending rows with `FOR UPDATE SKIP LOCKED`, publish, and mark status. Consumers record `(consumerName, eventId)` and business operation uniqueness in PostgreSQL.

## Consequences

- Database state and intent-to-publish commit together.
- Ambiguous publishes can duplicate messages; consumers must remain idempotent.
- Failed outbox rows remain inspectable/recoverable and require alerting.
- Broker duplicate detection is useful but never the correctness mechanism.
