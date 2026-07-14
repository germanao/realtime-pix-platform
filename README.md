# Real-Time PIX Event Platform

An educational, real-time money-transfer platform built with .NET 10, Next.js 16, PostgreSQL, Azure Service Bus, SignalR, and Terraform. It demonstrates an orchestrated Saga across two independently transactional fictional banks. It does not integrate with real PIX or hold real money.

Users join anonymously, receive accounts at two banks, deposit fictional funds, transfer to other participants or bots, and watch persisted Saga transitions move through the system in real time.

## What This Demonstrates

- Seven independently deployable workloads from one monorepo.
- An orchestrated Saga with debit, credit, compensation, timeout, and manual-intervention paths.
- Database-per-service ownership: Identity, Bank A, Bank B, Transaction, and Realtime Projection.
- Transactional EF Core outboxes, durable inbox deduplication, optimistic Saga concurrency, and atomic conditional debits.
- CloudEvents 1.0-style, versioned integration contracts over Azure Service Bus.
- Clean Architecture dependency rules enforced by xUnit.
- Azure infrastructure split into bootstrap, foundation, and runtime Terraform states.
- A validated but intentionally non-deployed private production reference profile.
- Dependency-aware readiness for PostgreSQL, Service Bus, Azure SignalR, Gateway dependencies, and Bot dependencies.

## Runtime Topology

| Workload | Responsibility | State owner |
| --- | --- | --- |
| API Gateway | Public BFF, wallet aggregation, bank routing | None |
| Identity/Presence | Anonymous sessions and live connections | Identity DB |
| Bank A Ledger | Bank A balances and append-only ledger | Bank A DB |
| Bank B Ledger | Bank B balances and append-only ledger | Bank B DB |
| Transaction | Saga orchestration, deadlines, idempotency | Transaction DB |
| Realtime Events | Timeline and transfer-flow projections | Realtime DB |
| Bot Worker | Always-available demo participants | None |

The same `bank-ledger-service` image is deployed twice with different bank configuration, identity, queue, and database. See [the architecture guide](docs/architecture/README.md) and [decision records](docs/architecture/decisions/README.md).

## Run Locally

Prerequisites: .NET 10 SDK, Node.js 24, and npm. Docker is optional for the default in-memory/file mode and required for integration tests.

```powershell
git clone https://github.com/germanao/realtime-pix-platform.git
cd realtime-pix-platform

dotnet restore RealtimePixPlatform.slnx
dotnet build RealtimePixPlatform.slnx --configuration Release

cd apps/web
npm ci
npm test -- --run
npm run build
cd ../..

node scripts/start-local.mjs --frontend
```

Open `http://localhost:3000`. Logs are written under `work/runtime-logs`; local event envelopes are under `work/local-bus`. Stop all local processes with:

```powershell
node scripts/stop-local.mjs
```

The local default uses in-memory repositories and the JSONL event-bus adapter. Azure uses PostgreSQL, Service Bus, managed identity, and Azure SignalR through the same application ports.

To run all five local PostgreSQL databases, apply migrations, and inject their connections automatically, start Docker Desktop and add `--with-postgres`. Stop those containers with `node scripts/stop-local.mjs --with-postgres`.

## Verify

```powershell
dotnet test RealtimePixPlatform.slnx --configuration Release

cd apps/web
npm test -- --run
npm run build
```

Run the Docker-backed PostgreSQL migration, concurrency, outbox/inbox, and official Service Bus emulator suite with:

```powershell
$env:RUN_INTEGRATION_TESTS = "true"
dotnet test tests/RealtimePix.IntegrationTests/RealtimePix.IntegrationTests.csproj --configuration Release
```

CI also builds every deployable Docker image, validates/tests Terraform, runs architecture policies, and scans dependencies and infrastructure configuration.

## Cloud Deployment

The POC backend runs in Azure and the frontend deploys to Vercel. GitHub Actions authenticates to Azure through OIDC; no Azure client secret is stored in GitHub.

- [Current deployment index](docs/deployment/README.md)
- [Azure and Terraform topology](docs/architecture/cloud-provisioning.md)
- [Terraform stacks](infra/terraform)
- [Production reference limitations](infra/terraform/production-reference/README.md)
