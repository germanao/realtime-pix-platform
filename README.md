# Real-Time PIX Platform

[Live demo](https://realtime-pix-web.vercel.app) · [.NET 10](https://dotnet.microsoft.com/) · [Next.js 16](https://nextjs.org/) · PostgreSQL · SignalR · Terraform

An educational, real-time money-transfer platform that demonstrates an orchestrated Saga across two independently transactional fictional banks. It does not connect to Brazil's real PIX network and never holds real money.

## What it demonstrates

- Cross-bank debit, credit, compensation, and manual-intervention Saga paths.
- Transactional outbox/inbox delivery over a durable PostgreSQL event bus.
- Idempotent commands, optimistic concurrency, and ledger-level money invariants.
- Live presence and transfer visualization over ASP.NET Core SignalR.
- Clean Architecture boundaries enforced by automated tests.
- An on-demand AWS demo runtime that starts from the browser and stops after inactivity.

## Production topology

The public frontend runs on Vercel. A CloudFront endpoint wakes an EC2-hosted Docker Compose runtime in `us-east-2`; nginx routes the API and direct SignalR connections. PostgreSQL 16 stores five service-owned databases plus the durable event bus. Images are published to GHCR. AWS SSM holds the runtime environment and S3 stores encrypted database backups and Terraform state.

Azure is not part of the running application. The repository intentionally contains only the current AWS/Vercel deployment path.

See [architecture](docs/architecture/README.md), [deployment](docs/deployment/README.md), and [AWS runtime operations](infra/terraform/aws-runtime/README.md).

## Run locally

Prerequisites: .NET 10 SDK, Node.js 24, and npm. Docker is needed for PostgreSQL mode and integration tests.

```powershell
dotnet restore RealtimePixPlatform.slnx
dotnet build RealtimePixPlatform.slnx -c Release
dotnet test RealtimePixPlatform.slnx -c Release --no-build

cd apps/web
npm ci
npm test -- --run
npm run build
```

Start all services with the lightweight file transport:

```powershell
node scripts/start-local.mjs --frontend
```

Use `--with-postgres` to start the local PostgreSQL databases and apply EF migrations before launching the services.

## Repository map

| Path | Purpose |
| --- | --- |
| `apps/web` | Next.js user experience and Playwright tests |
| `services` | API Gateway, Identity/Presence, two-bank Ledger host, Transaction Saga, Realtime Events, and Bot worker |
| `building-blocks/dotnet` | Shared eventing, persistence, and web-hosting adapters |
| `contracts/dotnet` | Versioned integration contracts |
| `infra/terraform/aws-runtime` | AWS on-demand runtime, controller, compose file, and backups |
| `tests` | Unit, architecture, hosting, and PostgreSQL integration tests |

## Safety and scope

The deployment is a small public demo, not a production banking system. It uses anonymous identities, synthetic balances, a single host, one replica per service, and a shared daily runtime allowance. Do not use it for personal data, real credentials, real financial transactions, or availability-sensitive workloads.

## License

See [LICENSE](LICENSE).
