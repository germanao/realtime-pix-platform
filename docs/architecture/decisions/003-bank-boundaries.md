# ADR-003: Deploy One Bank Ledger Codebase Twice

**Status:** Accepted

## Context

A meaningful cross-bank Saga needs independently transactional participants, but duplicating Bank A and Bank B source would create needless drift.

## Decision

Deploy `bank-ledger-service` twice. `Bank:Id`, `Bank:Name`, database, command queue, and managed identity define each bank boundary. Gateway aggregates account reads and routes account commands by explicit `bankId`.

## Consequences

- Bank behavior is consistent while data and permissions remain isolated.
- A shared image release affects both banks and therefore needs cross-bank tests.
- The POC shares one PostgreSQL server for cost, but databases and transactions remain separate.
- Production reference uses separate private PostgreSQL servers.
