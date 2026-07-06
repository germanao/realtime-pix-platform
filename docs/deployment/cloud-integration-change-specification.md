# Cloud Integration Change Specification

This document defines the code and repository changes required to make the
Real-Time PIX Event Platform function correctly across Azure Container Apps,
Azure Event Grid, Azure SignalR Service, Neon PostgreSQL, GHCR, and Vercel.

It describes implementation work. Provisioning instructions are in
[Recommended Cloud Services Provisioning Runbook](recommended-cloud-services-provisioning.md).

Last reviewed against the repository: June 18, 2026.

## 1. Current State

The repository currently has six independently runnable .NET 10 services:

- `services/api-gateway`
- `services/identity-presence-service`
- `services/wallet-ledger-service`
- `services/transaction-service`
- `services/realtime-events-service`
- `services/bot-service`

The cloud blockers are:

| Area | Current implementation | Required change |
| --- | --- | --- |
| Event transport | Shared `events.jsonl` file | Azure Event Grid adapter |
| Publication reliability | Direct publish after memory mutation | Transactional outbox |
| Consumer reliability | File offset and memory behavior | PostgreSQL inbox |
| Identity state | `PresenceStore` in memory | Identity PostgreSQL repository |
| Wallet state | `WalletLedgerStore` in memory | Wallet PostgreSQL ledger |
| Transfer state | `TransferStore` in memory | Transaction PostgreSQL saga state |
| Realtime state | `RealtimeProjectionStore` in memory | Realtime projection PostgreSQL |
| SignalR | In-process hub fanout | Azure SignalR Service |
| Configuration | Local URLs/file paths | Typed cloud options and secrets |
| Packaging | No production Dockerfiles | Six multi-stage Dockerfiles |
| Health | Single shallow `/health` | Startup/live/ready checks |
| Telemetry | Console logs | OpenTelemetry and Application Insights |

The local implementation must continue to work without Azure credentials.

## 2. Target Runtime Modes

Support two explicit runtime modes.

### 2.1 Local development

```text
EventBus__Provider=File
Persistence__Provider=InMemory
Realtime__Provider=LocalSignalR
```

This mode keeps startup simple and preserves the existing local experience.
The local PostgreSQL Docker Compose profile may be used for integration tests
or a more production-like local mode.

### 2.2 Cloud

```text
EventBus__Provider=EventGrid
Persistence__Provider=Postgres
Realtime__Provider=AzureSignalR
```

Cloud startup must fail clearly when a required option or secret is missing.
It must never silently fall back to local files or in-memory state in
`Production`.

## 3. Target Repository Structure

Refactor incrementally toward this structure:

```text
building-blocks/dotnet/
|-- RealtimePix.Eventing/
|   |-- Abstractions/
|   |-- File/
|   |-- EventGrid/
|   |-- Inbox/
|   `-- Outbox/
|-- RealtimePix.Observability/
|-- RealtimePix.Persistence/
`-- RealtimePix.ServiceDefaults/

services/<service>/
|-- Api/
|-- Application/
|-- Domain/
|-- Infrastructure/
|   |-- Persistence/
|   |-- Eventing/
|   `-- Realtime/
|-- Program.cs
|-- appsettings.json
|-- appsettings.Development.json
`-- Dockerfile

infra/
|-- docker-compose.yml
|-- azure/
|   `-- terraform/
`-- database/
    |-- identity/
    |-- wallet/
    |-- transaction/
    `-- realtime/
```

Do not create one shared domain or shared EF project. The only shared
application-level artifacts should remain stable integration contracts and
technical building blocks.

## 4. NuGet Dependencies

Add stable .NET 10-compatible versions of the following packages where needed.
Central package version management may be introduced if the repository adopts
it consistently.

### 4.1 Eventing building block

```text
Azure.Messaging.EventGrid
Azure.Identity
Microsoft.Extensions.Azure
Npgsql.EntityFrameworkCore.PostgreSQL
```

The Event Grid publisher can initially use an access key. `Azure.Identity`
supports the managed-identity hardening path.

### 4.2 Database-owning services

```text
Microsoft.EntityFrameworkCore
Microsoft.EntityFrameworkCore.Design
Npgsql.EntityFrameworkCore.PostgreSQL
```

Add `Microsoft.EntityFrameworkCore.Design` as a private asset used for tooling.

### 4.3 SignalR services

Add to identity/presence and realtime-events:

```text
Microsoft.Azure.SignalR
```

### 4.4 Observability

Use OpenTelemetry packages appropriate to the final exporter choice:

```text
OpenTelemetry.Extensions.Hosting
OpenTelemetry.Instrumentation.AspNetCore
OpenTelemetry.Instrumentation.Http
OpenTelemetry.Instrumentation.Runtime
OpenTelemetry.Exporter.OpenTelemetryProtocol
Azure.Monitor.OpenTelemetry.AspNetCore
```

Prefer one Azure Monitor/OpenTelemetry setup path. Do not register competing
exporters that duplicate every trace.

## 5. Eventing Abstraction Changes

### 5.1 Replace file-specific registration

Current services call:

```csharp
AddRealtimePixFileEventBus(configuration, consumerName)
```

Replace that public registration surface with:

```csharp
AddRealtimePixEventing(configuration, consumerName)
```

The extension chooses a provider from `EventBus:Provider`.

Expected values:

```text
File
EventGrid
```

Unknown values must fail startup with an actionable configuration error.

### 5.2 Keep the file provider

Move the existing classes under a `File` namespace/folder:

- `FileIntegrationEventPublisher`
- `FileEventBusWorker`
- `FileEventBusOptions`

The file provider remains a local adapter, not the cloud implementation.

### 5.3 Add Event Grid options

Add a validated options class:

```csharp
public sealed class EventGridOptions
{
    public const string SectionName = "EventGrid";

    public required Uri TopicEndpoint { get; init; }
    public string? AccessKey { get; init; }
    public string? WebhookSecret { get; init; }
    public required string ConsumerName { get; init; }
    public bool UseManagedIdentity { get; init; }
}
```

Validate:

- HTTPS topic endpoint in Production.
- Exactly one publishing credential mode.
- Nonempty consumer name on consumer services.
- Nonempty webhook secret when webhook authentication is enabled.

### 5.4 CloudEvent mapping

Publish an actual CloudEvents v1.0 event.

Map the current `EventEnvelope` fields as follows:

| Current field | CloudEvent location |
| --- | --- |
| `EventId` | `id` |
| `EventType` | `type` |
| `OccurredAt` | `time` |
| `Producer` | `source`, for example `/realtime-pix/wallet-ledger-service` |
| Aggregate identifier | `subject`, when available |
| `Version` | data metadata or extension attribute |
| `CorrelationId` | data metadata or extension attribute |
| `CausationId` | data metadata or extension attribute |
| Payload | `data.payload` |

Recommended data shape:

```json
{
  "version": 1,
  "correlationId": "transfer-id-or-trace-id",
  "causationId": "previous-event-id",
  "producer": "wallet-ledger-service",
  "payload": {}
}
```

Consumers must convert the CloudEvent back into the existing internal
`EventEnvelope` before invoking `IIntegrationEventHandler`.

### 5.5 Event Grid publisher

Add `EventGridIntegrationEventPublisher`.

Responsibilities:

1. Create `EventGridPublisherClient` once through dependency injection.
2. Convert an outbox message to a CloudEvent.
3. Publish with the original application event ID.
4. Preserve correlation and causation metadata.
5. Respect cancellation.
6. Log event type, ID, producer, correlation ID, and attempt number.
7. Never log full financial payloads or credentials.

The publisher must not decide that an event is permanently delivered merely
because subscribers later process it. A successful Event Grid publish means
the event was accepted by Event Grid.

## 6. Transactional Outbox

The current `IIntegrationEventPublisher` publishes directly. That is unsafe
when state is moved to PostgreSQL:

1. Database commit could succeed.
2. Process could stop before Event Grid publish.
3. The state would exist but no integration event would be emitted.

### 6.1 Outbox write model

Every service that changes state and emits events must insert its outbox row in
the same PostgreSQL transaction as the domain state change.

Use a table equivalent to:

```sql
CREATE TABLE integration_outbox (
    id uuid PRIMARY KEY,
    event_type text NOT NULL,
    event_version integer NOT NULL,
    producer text NOT NULL,
    correlation_id text NOT NULL,
    causation_id text NULL,
    subject text NULL,
    payload_json jsonb NOT NULL,
    occurred_at timestamptz NOT NULL,
    published_at timestamptz NULL,
    publish_attempts integer NOT NULL DEFAULT 0,
    next_attempt_at timestamptz NULL,
    last_error text NULL
);

CREATE INDEX ix_integration_outbox_pending
    ON integration_outbox (next_attempt_at, occurred_at)
    WHERE published_at IS NULL;
```

### 6.2 Outbox dispatcher

Add a `BackgroundService` per publishing service.

Behavior:

1. Read a small batch of unpublished rows.
2. Lock rows with `FOR UPDATE SKIP LOCKED`, or use an equivalent EF-safe
   concurrency mechanism.
3. Publish each event to Event Grid.
4. Mark `published_at` only after Event Grid accepts it.
5. Increment attempts and calculate bounded exponential backoff on failure.
6. Leave failed rows available for later retry.
7. Emit metrics for pending age, attempts, success, and failure.
8. Support graceful shutdown.

Do not delete outbox rows immediately. Retain them for a defined audit period
and clean them with a controlled job.

### 6.3 Application API

Replace immediate publishing inside business operations with an outbox
enqueue abstraction, for example:

```csharp
public interface IIntegrationEventOutbox
{
    void Add<TPayload>(
        string eventType,
        int version,
        string producer,
        TPayload payload,
        string correlationId,
        string? causationId = null,
        string? subject = null);
}
```

The implementation attaches an outbox entity to the service's current
`DbContext`; `SaveChangesAsync` commits business state and events together.

## 7. Idempotent Inbox

Event Grid is at-least-once and does not guarantee event order. Every consumer
must be replay safe.

### 7.1 Inbox table

Use:

```sql
CREATE TABLE integration_inbox (
    event_id uuid NOT NULL,
    consumer_name text NOT NULL,
    event_type text NOT NULL,
    received_at timestamptz NOT NULL,
    processed_at timestamptz NULL,
    error text NULL,
    attempts integer NOT NULL DEFAULT 0,
    PRIMARY KEY (event_id, consumer_name)
);
```

The composite primary key is the final deduplication boundary.

### 7.2 Processing transaction

For each webhook event:

1. Authenticate the request.
2. Parse the CloudEvent.
3. Begin a database transaction.
4. Insert the inbox row.
5. If the insert conflicts with the primary key, return success without
   repeating business behavior.
6. Invoke the matching integration handler.
7. Persist business/projection changes.
8. Add any resulting outbox events.
9. Mark the inbox row processed.
10. Commit.
11. Return HTTP success.

If processing fails, roll back. Event Grid can retry the original delivery.

### 7.3 Out-of-order behavior

Handlers must tolerate these cases:

- Credit succeeds after completion was already recorded.
- Debit failure is replayed after a transfer is failed.
- Presence offline arrives after a newer online lease.
- A timeline event arrives before an earlier event.

Store event occurrence time and state version where ordering matters. Never
overwrite a newer state with an older event only because it was delivered
later.

## 8. Event Grid Webhook Endpoint

Add this endpoint to:

- `wallet-ledger-service`
- `transaction-service`
- `realtime-events-service`

Path:

```text
POST /integration-events/eventgrid
```

### 8.1 Endpoint responsibilities

1. Enforce HTTPS in Production.
2. Validate the configured authorization header using constant-time
   comparison.
3. Complete Event Grid endpoint validation for the configured CloudEvents
   delivery mode.
4. Reject bodies above an explicit size limit.
5. Parse a batch of CloudEvents.
6. Reject malformed event IDs/types with HTTP 400.
7. Dispatch only event types registered by local handlers.
8. Process each event through the inbox transaction.
9. Return success for already processed event IDs.
10. Produce structured logs without logging secrets.

### 8.2 Authentication

The POC Event Grid subscriptions send:

```text
Authorization: Bearer <per-subscription-secret>
```

Store the expected value in Container App secrets.

This is appropriate for a first public webhook deployment. The hardening path
is Microsoft Entra-protected webhooks or a durable broker endpoint that
supports managed identity.

### 8.3 Endpoint exposure

Event Grid Basic webhook delivery needs a publicly reachable HTTPS endpoint.
Wallet and transaction Container Apps therefore require external ingress even
though their business APIs are intended to be gateway-only.

Add authorization middleware or endpoint filters so that:

- Event Grid callback accepts only the expected webhook credential.
- Business write endpoints accept only gateway/service credentials.
- Health endpoints expose no sensitive dependency details.

Do not assume that "not linked from the UI" protects a public endpoint.

## 9. Event Subscription Matrix

Configure:

| Subscription | Consumer | Event types |
| --- | --- | --- |
| `sub-wallet-ledger` | wallet-ledger | `PixTransferRequested.v1` |
| `sub-transaction` | transaction | `PixDebitSucceeded.v1`, `PixDebitFailed.v1`, `PixCreditSucceeded.v1` |
| `sub-realtime-events` | realtime-events | All platform integration events |

The identity and bot services publish events but do not currently consume
Event Grid events.

The API gateway neither publishes nor consumes integration events.

## 10. Identity and Presence Persistence

Replace `PresenceStore` with application interfaces and PostgreSQL
implementations.

### 10.1 Tables

Use an initial schema equivalent to:

```text
anonymous_users
- id uuid primary key
- client_id text unique not null
- display_name text not null
- is_bot boolean not null
- created_at timestamptz not null
- last_seen_at timestamptz not null

user_sessions
- id uuid primary key
- user_id uuid not null
- created_at timestamptz not null
- last_seen_at timestamptz not null
- expires_at timestamptz not null

presence_connections
- connection_id text primary key
- user_id uuid not null
- connected_at timestamptz not null
- last_heartbeat_at timestamptz not null
- disconnected_at timestamptz null
- server_instance text null

integration_outbox
```

Identity currently does not require an inbox table, but adding the common table
is acceptable if service defaults expect it.

### 10.2 Online calculation

A normal user is online when at least one connection:

- Has no `disconnected_at`.
- Has a heartbeat inside the configured lease duration.

A bot identity is always reported online unless the product later distinguishes
configured bots from actively transacting bots.

### 10.3 Hub lifecycle

On hub connect:

1. Validate the anonymous session/user token.
2. Insert the connection row.
3. Determine whether this is the first live connection for the user.
4. Emit `UserPresenceChanged.v1` only on transition from offline to online.

On hub disconnect:

1. Mark the specific connection disconnected.
2. Count remaining live connections.
3. Emit offline only when the last connection is gone.

Add a heartbeat method and stale-lease cleanup worker for browser/process
failures where disconnect callbacks are delayed.

Use a short grace period to avoid online/offline flicker during automatic
SignalR reconnect.

### 10.4 Identity idempotency

`POST /sessions/anonymous` with the same stable client ID must return the same
anonymous user unless the session has intentionally expired.

Random display-name assignment must be persisted, not regenerated on every
service restart.

## 11. Wallet and Ledger Persistence

Replace `WalletLedgerStore` with a relational ledger.

### 11.1 Tables

```text
wallet_users
- user_id uuid primary key
- created_at timestamptz not null

accounts
- id uuid primary key
- user_id uuid not null
- bank_code text not null
- display_name text not null
- currency char(3) not null
- current_balance numeric(18,2) not null
- version bigint not null
- created_at timestamptz not null
- unique(user_id, bank_code)

ledger_entries
- id uuid primary key
- account_id uuid not null
- entry_type text not null
- amount numeric(18,2) not null
- balance_after numeric(18,2) not null
- transfer_id uuid null
- source_event_id uuid null
- description text not null
- occurred_at timestamptz not null
- unique(source_event_id, account_id, entry_type)

processed_transfers
- transfer_id uuid primary key
- request_event_id uuid unique not null
- debit_entry_id uuid null
- credit_entry_id uuid null
- status text not null
- processed_at timestamptz not null

integration_inbox
integration_outbox
```

### 11.2 Money rules

- Use `numeric(18,2)` or a deliberately selected precision.
- Use `decimal` in C#.
- Never use `double` for balances.
- Amount must be positive.
- Debit and credit entries are immutable.
- Corrections are compensating entries, not row edits.
- The account row may cache the current balance, but ledger entries remain the
  auditable source of changes.

### 11.3 Atomic transfer handling

When consuming `PixTransferRequested.v1`:

1. Inbox-deduplicate the event.
2. Lock the sender and recipient account rows in deterministic ID order.
3. Verify both accounts exist.
4. Verify the sender has sufficient funds.
5. On failure, create one `PixDebitFailed.v1` outbox event.
6. On success, create one debit and one credit entry.
7. Update cached balances and versions.
8. Create `PixDebitSucceeded.v1` and `PixCreditSucceeded.v1` outbox events.
9. Commit everything in one database transaction.

The existing educational flow emits separate debit and credit events from one
wallet handler. Preserve that contract unless the saga is intentionally
redesigned.

### 11.4 Concurrency

Use row locks or optimistic concurrency with a version column. Tests must cover
two concurrent transfer requests attempting to spend the same funds.

## 12. Transaction Saga Persistence

Replace `TransferStore` with a persisted transfer aggregate.

### 12.1 Tables

```text
pix_transfers
- id uuid primary key
- idempotency_key text unique not null
- sender_user_id uuid not null
- sender_account_id uuid not null
- recipient_user_id uuid not null
- recipient_account_id uuid not null
- amount numeric(18,2) not null
- status text not null
- requested_at timestamptz not null
- debit_succeeded_at timestamptz null
- credit_succeeded_at timestamptz null
- completed_at timestamptz null
- failed_at timestamptz null
- failure_code text null
- version bigint not null

transfer_event_transitions
- id uuid primary key
- transfer_id uuid not null
- source_event_id uuid unique not null
- from_status text not null
- to_status text not null
- occurred_at timestamptz not null

integration_inbox
integration_outbox
```

### 12.2 Transfer creation

`POST /pix/transfers` must:

1. Require an idempotency key.
2. Validate sender, recipient, accounts, and amount.
3. Open a transaction.
4. Insert the transfer with status `requested`.
5. Insert `PixTransferRequested.v1` into the outbox.
6. Commit.
7. If the idempotency key already exists, return the existing transfer without
   creating a second outbox message.

Use a database unique constraint as the final idempotency guarantee. An
in-memory guard is insufficient across replicas.

### 12.3 Saga transitions

Handle transitions as monotonic state changes:

```text
requested -> debit_succeeded -> completed
requested -> failed
debit_succeeded -> failed, only for a defined compensating path
```

Replayed success/failure events must not create additional terminal events.

When the first valid credit-success event reaches the saga:

1. Mark the transfer completed.
2. Add `PixTransferCompleted.v1` to the outbox.
3. Commit once.

## 13. Realtime Projection Persistence

Replace `RealtimeProjectionStore` with a durable read model.

### 13.1 Tables

```text
event_timeline
- event_id uuid primary key
- event_type text not null
- producer text not null
- correlation_id text not null
- transfer_id uuid null
- occurred_at timestamptz not null
- received_at timestamptz not null
- summary text not null
- status text not null

transfer_flow_steps
- id uuid primary key
- transfer_id uuid not null
- source_event_id uuid not null
- step_type text not null
- service_name text not null
- status text not null
- explanation text not null
- occurred_at timestamptz not null
- unique(transfer_id, source_event_id, step_type)

integration_inbox
integration_outbox
```

### 13.2 Projection flow

For every accepted domain event:

1. Insert one timeline item keyed by source `eventId`.
2. If transfer-related, insert one flow step with a unique source-event
   constraint.
3. Optionally add one `ArchitectureFlowStepRecorded.v1` outbox event.
4. Commit.
5. Push the committed projection to SignalR clients.

Never push to clients before the projection transaction commits.

### 13.3 Avoid architecture-event loops

If `ArchitectureFlowStepRecorded.v1` remains an Event Grid event:

- Do not generate another architecture event while handling it.
- Include `sourceEventId` in the payload.
- Deduplicate the flow step by source event and step type.

## 14. Azure SignalR Integration

The browser continues to connect to the existing application hub routes:

```text
/presence/hub
/events/hub
```

The application negotiate endpoint redirects the client to Azure SignalR.

### 14.1 Identity service

Replace:

```csharp
builder.Services.AddSignalR();
```

with conditional registration:

```csharp
var signalR = builder.Services.AddSignalR();

if (builder.Configuration["Realtime:Provider"] == "AzureSignalR")
{
    signalR.AddAzureSignalR(options =>
    {
        options.ApplicationName =
            builder.Configuration["Azure:SignalR:ApplicationName"]
            ?? "realtime-pix-presence";
    });
}
```

Use the default connection configuration:

```text
Azure:SignalR:ConnectionString
```

### 14.2 Realtime service

Use a different application name:

```text
realtime-pix-events
```

This prevents accidental namespace overlap when both services use one Azure
SignalR resource.

Set it through:

```text
Azure:SignalR:ApplicationName
```

### 14.3 Frontend

The existing `@microsoft/signalr` client remains appropriate.

Keep:

```text
NEXT_PUBLIC_PRESENCE_HUB_URL
NEXT_PUBLIC_EVENTS_HUB_URL
```

Required frontend behavior:

- Automatic reconnect with bounded backoff.
- One connection instance per mounted application session.
- Stable event handler registration and cleanup.
- Full HTTP resync after reconnect.
- Deduplication of timeline and flow events by `eventId`.
- A visible reconnecting/offline state without console error loops.

Do not store the Azure SignalR connection string in Vercel. Browsers connect
through the ASP.NET Core hub endpoint, not with service credentials.

## 15. API Gateway and Service Security

### 15.1 Service URLs

Keep the current configuration shape:

```text
Services__IdentityPresence=http://pix-identity-presence
Services__WalletLedger=http://pix-wallet-ledger
Services__Transaction=http://pix-transaction
Services__RealtimeEvents=http://pix-realtime-events
```

Container Apps resolves these names inside the same environment.

### 15.2 CORS

Replace permissive `AddCors()` calls with named policies bound to:

```text
Cors:AllowedOrigins
```

In Production:

- Require explicit HTTPS origins.
- Allow the exact Vercel production/custom domain.
- Allow credentials only on SignalR services when required.
- Do not combine wildcard origins with credentials.

### 15.3 Internal API authentication

Because wallet and transaction require external ingress for Event Grid
webhooks, protect their business endpoints.

Minimum POC:

- Require a gateway-to-service API key on business routes.
- Store the key in Container App secrets.
- Do not use the same key as the Event Grid webhook secret.

Production hardening:

- Use managed identity and Entra-issued tokens between services.
- Validate audience and issuer in each service.

## 16. Configuration Contract

### 16.1 Shared settings

```json
{
  "EventBus": {
    "Provider": "File"
  },
  "EventGrid": {
    "TopicEndpoint": "",
    "AccessKey": "",
    "WebhookSecret": "",
    "ConsumerName": "",
    "UseManagedIdentity": false
  },
  "Persistence": {
    "Provider": "InMemory"
  },
  "Realtime": {
    "Provider": "LocalSignalR"
  },
  "Cors": {
    "AllowedOrigins": [
      "http://localhost:3000"
    ]
  }
}
```

### 16.2 Environment variable matrix

| Variable | Gateway | Identity | Wallet | Transaction | Realtime | Bot |
| --- | :---: | :---: | :---: | :---: | :---: | :---: |
| `EventBus__Provider` | No | Yes | Yes | Yes | Yes | Yes |
| `EventGrid__TopicEndpoint` | No | Yes | Yes | Yes | Yes | Yes |
| `EventGrid__AccessKey` | No | Yes | Yes | Yes | Yes | Yes |
| `EventGrid__WebhookSecret` | No | No | Yes | Yes | Yes | No |
| `ConnectionStrings__IdentityPresence` | No | Yes | No | No | No | No |
| `ConnectionStrings__WalletLedger` | No | No | Yes | No | No | No |
| `ConnectionStrings__Transaction` | No | No | No | Yes | No | No |
| `ConnectionStrings__RealtimeProjection` | No | No | No | No | Yes | No |
| `Azure__SignalR__ConnectionString` | No | Yes | No | No | Yes | No |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | Yes | Yes | Yes | Yes | Yes | Yes |

Reduce the topic key distribution when managed-identity publication is
implemented.

### 16.3 Secret rules

- Do not put secrets in `appsettings.json`.
- Do not put secrets in committed Terraform variable files.
- Do not use `NEXT_PUBLIC_` for secrets.
- Do not print configuration objects at startup.
- Rotate Event Grid, SignalR, database, and service API credentials after any
  accidental exposure.

## 17. Health Checks

Expose:

```text
GET /health/startup
GET /health/live
GET /health/ready
```

### 17.1 Startup

Indicates configuration loaded and application initialization completed.

### 17.2 Liveness

Indicates the process event loop is responsive. It should not fail merely
because a remote dependency has a temporary outage, or Container Apps will
restart healthy processes repeatedly.

### 17.3 Readiness

Check:

- Required PostgreSQL database.
- Ability to query the migration history/schema version.
- Required internal HTTP dependency only when the service cannot operate
  safely without it.

Outbox backlog age should be a metric and alert. Decide carefully before making
Event Grid publication a synchronous readiness dependency.

## 18. Observability

### 18.1 Trace propagation

Use W3C trace context over:

- Browser to gateway.
- Gateway to internal HTTP services.
- Outbox to Event Grid metadata.
- Event Grid callback to consumer processing.
- SignalR projection publication.

Set `correlationId` to the transfer ID for transfer workflows. Preserve
`causationId` as the source event ID.

### 18.2 Service names

Configure:

```text
realtime-pix-api-gateway
realtime-pix-identity-presence
realtime-pix-wallet-ledger
realtime-pix-transaction
realtime-pix-realtime-events
realtime-pix-bot
```

### 18.3 Required metrics

- HTTP request count, duration, and failures.
- SignalR connection count and reconnects.
- Outbox pending count and oldest age.
- Outbox publish attempts/failures.
- Inbox duplicate count.
- Event handler duration/failures.
- Transfer requested/completed/failed count.
- Presence online user count.
- Database pool usage.

### 18.4 Logging rules

Structured log fields:

```text
service
environment
eventId
eventType
correlationId
causationId
transferId
userId
accountId
```

Do not log:

- Database passwords.
- Event Grid keys.
- SignalR connection strings.
- Webhook bearer tokens.
- Full request bodies containing sensitive values.

## 19. Docker Changes

Add:

```text
.dockerignore
services/api-gateway/Dockerfile
services/identity-presence-service/Dockerfile
services/wallet-ledger-service/Dockerfile
services/transaction-service/Dockerfile
services/realtime-events-service/Dockerfile
services/bot-service/Dockerfile
```

### 19.1 Dockerfile requirements

- Build context is the monorepo root.
- Build stage uses `mcr.microsoft.com/dotnet/sdk:10.0`.
- Runtime uses `mcr.microsoft.com/dotnet/aspnet:10.0`.
- Restore project files before copying all source for layer caching.
- Publish Release with `UseAppHost=false`.
- Listen on port 8080.
- Run as a non-root user supported by the base image.
- Set no credentials in `ENV` instructions.
- Use `ENTRYPOINT ["dotnet", "<Service>.dll"]`.

### 19.2 `.dockerignore`

Exclude:

```text
**/bin
**/obj
**/node_modules
**/.next
.git
work
test-results
playwright-report
*.log
.env*
```

Do not exclude required solution, project, contract, or shared building-block
files.

### 19.3 Filesystem assumptions

Production code must not depend on:

```text
work/local-bus/events.jsonl
local disk offsets
container-local durable files
```

Container local storage is ephemeral.

## 20. Database Migration Delivery

Create EF Core migrations in each owning service or dedicated infrastructure
project.

Required commands should follow this shape:

```powershell
dotnet ef migrations add InitialIdentityPersistence `
  --project services/identity-presence-service `
  --startup-project services/identity-presence-service

dotnet ef database update `
  --project services/identity-presence-service `
  --startup-project services/identity-presence-service
```

Provide equivalent commands for wallet, transaction, and realtime.

Preferred cloud process:

1. CI builds migration bundles.
2. Deployment environment approval is granted.
3. One migration job runs per database.
4. Migration job succeeds.
5. Application revisions deploy.
6. Smoke tests run.

Do not automatically run schema migrations concurrently from every app
replica.

## 21. Terraform Changes

Extend `infra/azure/terraform` to manage:

- Resource group lookup or creation.
- Storage account and dead-letter container.
- Event Grid custom topic.
- Event Grid managed identity and storage role assignment.
- Azure SignalR Free/Standard resource.
- Log Analytics workspace.
- Application Insights.
- Container Apps environment.
- Six Container Apps.
- Container App secrets and secret references.
- Event Grid subscriptions after endpoint URLs are known.
- Outputs for gateway and hub URLs.

Do not manage Neon passwords in ordinary Terraform state for the first version.
Inject them from protected GitHub environment secrets, or evaluate the Neon
Terraform provider with a deliberate state-security plan.

### 21.1 Terraform dependency challenge

Event Grid subscriptions depend on live webhook endpoints that can complete
validation. Use one of:

1. Two Terraform stages:
   - Core infrastructure and Container Apps.
   - Event subscriptions after app deployment.
2. Separate Terraform modules/states.
3. A post-deployment CLI step for event subscriptions.

Do not create subscriptions before the callback code is deployed.

## 22. GitHub Actions Changes

The deployment workflow must:

1. Restore .NET dependencies.
2. Build with warnings as errors.
3. Run all backend tests.
4. Install frontend dependencies with the lock file.
5. Run frontend unit tests.
6. Build the Next.js app.
7. Build six container images.
8. Scan images and dependencies.
9. Push immutable SHA tags to GHCR.
10. Authenticate to Azure using OIDC.
11. Apply core Terraform.
12. Run database migrations.
13. Deploy/update Container App revisions.
14. Apply Event Grid subscriptions.
15. Run HTTP and event-driven smoke tests.
16. Promote or retain the deployment based on results.

Use a protected GitHub environment for Azure deployment approval.

### 22.1 Required GitHub secrets

```text
AZURE_CLIENT_ID
AZURE_TENANT_ID
AZURE_SUBSCRIPTION_ID
NEON_IDENTITY_CONNECTION_STRING
NEON_WALLET_CONNECTION_STRING
NEON_TRANSACTION_CONNECTION_STRING
NEON_REALTIME_CONNECTION_STRING
```

If Terraform or Azure Portal creates the following secrets, prefer fetching or
injecting them through Azure rather than duplicating them in GitHub:

```text
EVENTGRID_ACCESS_KEY
AZURE_SIGNALR_CONNECTION_STRING
```

## 23. Backend Automated Tests

The implementation is not accepted without automated coverage.

### 23.1 Event Grid adapter tests

- EventEnvelope maps to CloudEvent without losing IDs or metadata.
- CloudEvent maps back to EventEnvelope.
- Publisher sends the original application event ID.
- Webhook rejects invalid authorization.
- Webhook accepts endpoint validation.
- Webhook handles batched events.
- Unknown event types are handled according to policy.

### 23.2 Outbox tests

- Business state and outbox event commit atomically.
- Failed publication leaves the event pending.
- Successful publication marks it once.
- Two dispatchers do not publish the same locked row concurrently.
- Retry delay increases within configured bounds.
- Old published rows can be cleaned safely.

### 23.3 Inbox tests

- First delivery changes state.
- Duplicate event ID returns success without changing state.
- Same event ID can be processed by a different consumer.
- Failed transaction does not leave a falsely completed inbox row.
- Out-of-order event does not regress state.

### 23.4 Identity tests

- Stable client ID returns stable anonymous user.
- First connection emits online.
- Second tab does not emit another online transition.
- One tab disconnect keeps the user online.
- Last connection disconnect emits offline.
- Expired lease is cleaned and emits offline once.
- Reconnect during grace period avoids flicker.
- Bots remain visible.
- Restart preserves identities and names.

### 23.5 Wallet tests

- Bootstrap creates exactly two accounts once.
- Deposit creates one immutable ledger entry.
- Duplicate deposit command is idempotent if the API supports command keys.
- Insufficient funds emits one failure event.
- Duplicate transfer request does not debit twice.
- Concurrent spending cannot create a negative balance.
- Credit and debit values remain correct at decimal boundaries.
- Restart preserves balances and history.

### 23.6 Transaction tests

- One request creates one transfer and outbox event.
- Reused idempotency key returns the existing transfer.
- Concurrent duplicate requests create one transfer.
- Duplicate debit/credit events do not create duplicate terminal events.
- Failure becomes terminal once.
- Completion becomes terminal once.
- Restart preserves saga state.

### 23.7 Realtime tests

- Duplicate source event creates one timeline row.
- Duplicate source event creates one flow step.
- Architecture event does not cause a publication loop.
- SignalR push occurs after database commit.
- Reconnect snapshot contains durable timeline/flow data.

### 23.8 Integration infrastructure

Use Testcontainers for PostgreSQL-backed integration tests. Event Grid adapter
tests should use an HTTP test server or mocked Azure SDK transport, not the live
Azure service in every test run.

Add a separate optional cloud smoke-test workflow for real Azure resources.

## 24. Frontend Tests

Add or retain:

- Submit guard permits one transfer request per click.
- Stable idempotency key is reused for one retry attempt.
- Successful completion clears the active attempt key.
- Timeline reducer ignores duplicate event IDs.
- Flow reducer ignores duplicate event/step IDs.
- SignalR handler cleanup prevents duplicate subscriptions after rerender.
- Reconnect triggers HTTP snapshot refresh.
- Presence list updates from online/offline events.
- Browser console remains free of uncaught errors.

Playwright cloud test:

1. Open two isolated browser contexts.
2. Create two users.
3. Verify presence propagation.
4. Close one context.
5. Verify offline propagation.
6. Send one transfer.
7. Assert exactly one debit, credit, and completed flow step.

## 25. Rollout Sequence

Implement in this order to avoid an untestable large change.

### Phase 1: Technical foundations

1. Add provider-based configuration.
2. Add PostgreSQL and EF packages.
3. Add Event Grid package and CloudEvent conversion tests.
4. Add service-default health and telemetry registration.
5. Keep all default providers local.

### Phase 2: Transaction persistence

1. Persist transfers and idempotency keys.
2. Add transaction outbox/inbox.
3. Add migration and tests.
4. Keep file event bus locally.

### Phase 3: Wallet persistence

1. Persist accounts and ledger.
2. Add concurrency protection.
3. Add wallet outbox/inbox.
4. Add migration and tests.

### Phase 4: Identity persistence

1. Persist anonymous users and sessions.
2. Persist connection leases.
3. Add heartbeat cleanup.
4. Add migration and tests.

### Phase 5: Realtime persistence

1. Persist timeline and flow.
2. Add realtime inbox/outbox.
3. Add migration and tests.

### Phase 6: Event Grid

1. Add publisher adapter.
2. Add webhook callback.
3. Add authorization.
4. Test local callback behavior.
5. Deploy endpoints.
6. Create subscriptions.
7. Run replay/resilience tests.

### Phase 7: Azure SignalR

1. Add conditional Azure SignalR registration.
2. Test presence with multiple replicas.
3. Test reconnect and snapshot recovery.

### Phase 8: Containers and automation

1. Add Dockerfiles.
2. Add GHCR workflow.
3. Extend Terraform.
4. Add migration jobs.
5. Add deployment smoke tests.
6. Deploy Vercel frontend.

## 26. Definition of Done

The cloud integration is complete only when:

- [ ] `events.jsonl` is not used in Production.
- [ ] Every data-owning service uses its own Neon PostgreSQL database.
- [ ] Realtime projection survives restart.
- [ ] Every state change and emitted integration event commits atomically.
- [ ] Every consumer deduplicates by event ID and consumer.
- [ ] Event Grid duplicates cannot duplicate debit, credit, failure, completion,
      timeline, or flow records.
- [ ] One transfer idempotency key produces one transfer.
- [ ] Azure SignalR supports at least two app replicas.
- [ ] Multiple tabs preserve correct online state.
- [ ] Closing the final connection produces an offline transition.
- [ ] Six immutable images deploy independently.
- [ ] Readiness checks verify required PostgreSQL dependencies.
- [ ] Correlated traces show the complete transfer workflow.
- [ ] Database migrations are automated and serialized.
- [ ] GitHub Actions uses Azure OIDC rather than a client secret.
- [ ] Vercel contains only public frontend configuration.
- [ ] Backend, frontend, integration, and browser tests pass.

## 27. Expected File Changes

The implementation will likely add or modify:

```text
Directory.Packages.props
building-blocks/dotnet/RealtimePix.Eventing/**
building-blocks/dotnet/RealtimePix.Observability/**
building-blocks/dotnet/RealtimePix.ServiceDefaults/**

services/identity-presence-service/**
services/wallet-ledger-service/**
services/transaction-service/**
services/realtime-events-service/**
services/api-gateway/Program.cs
services/bot-service/Program.cs

tests/Eventing.Tests/**
tests/IdentityPresenceService.Tests/**
tests/WalletLedgerService.Tests/**
tests/TransactionService.Tests/**
tests/RealtimeEventsService.Tests/**

apps/web/src/**
apps/web/tests/**

.dockerignore
services/*/Dockerfile
.github/workflows/backend-azure.yml
infra/azure/terraform/**
```

This is a substantial implementation. Each phase should remain deployable and
testable before continuing to the next phase.

## 28. Official References

- [Azure Event Grid .NET samples](https://learn.microsoft.com/en-us/azure/event-grid/custom-event-quickstart-portal)
- [Event Grid delivery and retry](https://learn.microsoft.com/en-us/azure/event-grid/delivery-and-retry)
- [Event Grid endpoint validation](https://learn.microsoft.com/en-us/azure/event-grid/end-point-validation-event-grid-events-schema)
- [Event Grid custom delivery headers](https://learn.microsoft.com/en-us/azure/event-grid/delivery-properties)
- [Azure SignalR ASP.NET Core quickstart](https://learn.microsoft.com/en-us/azure/azure-signalr/signalr-quickstart-dotnet-core)
- [Npgsql EF Core provider](https://www.npgsql.org/efcore/)
- [ASP.NET Core health checks](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks)
- [OpenTelemetry on Azure](https://learn.microsoft.com/en-us/azure/azure-monitor/app/opentelemetry-enable)
- [Azure Container Apps microservice communication](https://learn.microsoft.com/en-us/azure/container-apps/communication-between-microservices)
- [GitHub OIDC for Azure](https://learn.microsoft.com/en-us/azure/developer/github/connect-from-azure-openid-connect)
