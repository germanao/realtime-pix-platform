# Architecture

This project is a monorepo, not a monolith. Each service has its own runtime process, API surface, and database ownership boundary.

## Runtime flow

1. The Next.js app calls `api-gateway`.
2. `api-gateway` forwards public requests to the owning backend service.
3. `transaction-service` accepts PIX transfer commands and publishes `PixTransferRequested.v1`.
4. `wallet-ledger-service` consumes transfer events, persists ledger changes, and publishes debit or credit outcome events.
5. `transaction-service` consumes outcome events and marks the transfer completed or failed.
6. `realtime-events-service` consumes every event and projects them into a timeline and transfer-specific architecture flow.
7. `bot-service` keeps bot users visible by publishing presence events.

## Service ownership

| Service | Owns | Does not own |
| --- | --- | --- |
| Identity Presence | anonymous identity, active users, bots | balances, transfers |
| Wallet Ledger | accounts, balances, ledger entries | user profile lifecycle, transfer saga state |
| Transaction | transfer command state, idempotency, saga status | account balances |
| Realtime Events | event timeline projection, architecture flow read model | source-of-truth business state |

## Event delivery

The local development adapter writes CloudEvents-style envelopes to `work/local-bus/events.jsonl`. This keeps event-driven behavior visible and testable without a paid cloud dependency. The production adapter boundary is `IIntegrationEventPublisher`, which is where Azure Event Grid publishing belongs.

