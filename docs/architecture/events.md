# Event Contracts

All integration events use the envelope from `RealtimePix.Eventing.EventEnvelope`.

Required metadata:

- `eventId`
- `eventType`
- `version`
- `occurredAt`
- `correlationId`
- `causationId`
- `producer`
- `payload`

Event payload records live in `contracts/dotnet/RealtimePix.Contracts`.

## Versioning rule

Do not mutate a published `.v1` event incompatibly. Add a `.v2` event type when consumers would break.

## Idempotency rule

Every consumer must treat event delivery as at-least-once. The SQL scripts include `inbox_messages` tables for durable idempotent processing when PostgreSQL persistence is wired in.

