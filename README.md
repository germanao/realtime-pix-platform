# Real-Time PIX Event Platform

Educational real-time financial transaction platform built as a .NET 10 microservices monorepo with a Next.js frontend.

The project demonstrates event-driven architecture through fictional PIX transfers. Users join anonymously, receive two fictional bank accounts, deposit demo funds, transfer money to active users or bots, and watch the transaction move through the architecture in real time.

## Architecture

- `apps/web`: Next.js React frontend.
- `services/api-gateway`: public backend-for-frontend API.
- `services/identity-presence-service`: anonymous sessions, active users, bots.
- `services/wallet-ledger-service`: fictional accounts, balances, deposits, ledger history.
- `services/transaction-service`: PIX transfer saga orchestration.
- `services/realtime-events-service`: live event timeline and architecture flow projection.
- `services/bot-service`: bot heartbeats and demo activity.
- `contracts/dotnet`: versioned .NET integration event contracts.
- `contracts/events`: human-readable event catalog.
- `building-blocks/dotnet`: shared eventing, outbox, inbox, and local event bus primitives.
- `infra`: local and cloud provisioning notes.

## Local backend ports

| Service | URL |
| --- | --- |
| API Gateway | `http://localhost:5100` |
| Identity Presence | `http://localhost:5101` |
| Wallet Ledger | `http://localhost:5102` |
| Transaction | `http://localhost:5103` |
| Realtime Events | `http://localhost:5104` |
| Bot Service | `http://localhost:5105` |

## First validation

```powershell
dotnet build RealtimePixPlatform.slnx
```

Start the local development stack:

```powershell
node scripts/start-local.mjs --skip-infra --frontend
```

Stop the local development stack:

```powershell
node scripts/stop-local.mjs
```

The frontend is scaffolded in `apps/web`; install dependencies when network access is available:

```powershell
cd apps/web
npm install
npm run dev
```

## Azure deployment

- [Deployment guide index](docs/deployment/README.md)
- [Recommended cloud services provisioning](docs/deployment/recommended-cloud-services-provisioning.md)
- [Cloud integration change specification](docs/deployment/cloud-integration-change-specification.md)
- [Manual Azure Portal proof of concept](docs/deployment/azure-container-apps-portal-poc.md)
- [Terraform, GitHub Actions, and Azure Pipelines](docs/deployment/azure-container-apps-terraform-cicd.md)
