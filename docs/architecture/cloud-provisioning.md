# Cloud Provisioning Defaults

Detailed execution guides:

- [Recommended cloud services provisioning](../deployment/recommended-cloud-services-provisioning.md)
- [Cloud integration change specification](../deployment/cloud-integration-change-specification.md)
- [Azure Portal proof of concept](../deployment/azure-container-apps-portal-poc.md)
- [Terraform and CI/CD](../deployment/azure-container-apps-terraform-cicd.md)

Provision the first public demo with free-tier-oriented services:

- Vercel Hobby for `apps/web`.
- Azure Container Apps Consumption for each .NET service.
- Azure Event Grid for integration event delivery.
- Azure SignalR Service for the existing ASP.NET Core SignalR hubs.
- Azure Notification Hubs Free for the future notification adapter.
- Neon PostgreSQL Free with four isolated projects:
  - `identity_presence_db`
  - `wallet_ledger_db`
  - `transaction_db`
  - `realtime_projection_db`

The realtime database is recommended so the public timeline and transfer flow
survive service restarts. It can be omitted only when that projection is
explicitly disposable.

## Container Apps

Each service should be deployed independently:

- `api-gateway`
- `identity-presence-service`
- `wallet-ledger-service`
- `transaction-service`
- `realtime-events-service`
- `bot-service`

Use scale-to-zero where possible. Keep only the realtime or bot workloads warm if the demo needs immediate responsiveness.

## Secrets

Keep these as environment variables or managed secrets:

- `ConnectionStrings__IdentityPresence`
- `ConnectionStrings__WalletLedger`
- `ConnectionStrings__Transaction`
- `ConnectionStrings__RealtimeProjection`
- `EventGrid__TopicEndpoint`
- `EventGrid__AccessKey`
- `Azure__SignalR__ConnectionString`
- `NotificationHubs__ConnectionString`
