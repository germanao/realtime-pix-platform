# Integration Contracts

Stable contracts live in `RealtimePix.Contracts`; the machine-readable catalog is `contracts/events/event-catalog.json`. Services do not share domain or persistence models.

## Envelope

`EventEnvelope` follows CloudEvents 1.0 naming and carries:

- `specVersion`, `eventId`, `eventType`, `version`, and `occurredAt`
- `producer`, `subject`, and `dataContentType`
- `correlationId`, `causationId`, and `traceParent`
- `messageKind`, `destinationKind`, and `destination`
- typed JSON `payload`

Commands target a bank queue. Integration events target `platform-events` and are filtered into consumer subscriptions.

## Saga Commands

| Command | Destination | Meaning |
| --- | --- | --- |
| `DebitFunds.v1` | Sender bank queue | Conditionally debit once |
| `CreditFunds.v1` | Recipient bank queue | Credit once |
| `RefundFunds.v1` | Sender bank queue | Apply compensation once |

## Saga Outcomes

`FundsDebited.v1`, `FundsDebitRejected.v1`, `FundsCredited.v1`, `FundsCreditRejected.v1`, `FundsRefunded.v1`, and `FundsRefundRejected.v1` drive coordinator transitions. `SagaTransitionRecorded.v1`, `PixSagaTimedOut.v1`, `PixTransferCompleted.v2`, `PixTransferFailed.v2`, and `PixTransferCompensated.v1` provide durable public/operational facts.

Legacy PIX `.v1` outcome events remain accepted by Realtime Projection for one compatibility release. New orchestration behavior uses the Saga outcome contracts above.

## Compatibility Rules

1. Never reinterpret or incompatibly mutate a published version.
2. Add optional fields only when old consumers remain valid; otherwise publish a new version.
3. Consumers must ignore unknown additive fields.
4. Publishers own schemas; consumers own replay-safe handling.
5. No ordering is assumed across entities. Saga state/version decides whether a message is currently applicable.
6. Replaying a processed message must not change balances, ledgers, Saga state, or projections.

Broker duplicate detection reduces noise but is not a correctness boundary. Transactional outbox, durable inbox, operation uniqueness, and Saga optimistic concurrency provide correctness across restarts and duplicate delivery.
