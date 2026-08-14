# Free-Tier Deployment Profile

This profile is designed for an eligible Azure free account plus Vercel Hobby. It preserves the distributed Saga architecture while avoiding always-on container allocation.

## Included services

| Capability | Resource | Guardrail |
| --- | --- | --- |
| Compute | Azure Container Apps Consumption | Zero minimum replicas and one maximum replica per app |
| Bot seeding | Azure Container Apps Job | Runs only during deployment |
| Database | PostgreSQL Flexible Server B1ms | One server, 32 GB, five active databases |
| Messaging | Azure Service Bus Standard | One namespace, under 13 million operations/month |
| Images | Public GitHub Container Registry | Public packages; no Azure registry |
| Realtime | Azure SignalR Free | One unit, 20 concurrent connections |
| API edge | API Management Consumption | Under one million calls/month |
| Configuration | App Configuration Free | One free store |
| Telemetry | Log Analytics and Application Insights | 0.1 GB/day ingestion cap; 30-day retention |
| Frontend | Vercel Hobby | One Next.js project |

Azure's Container Apps grant is always available up to its monthly request/CPU/memory limits. PostgreSQL and Service Bus allowances expire 12 months after the Azure free account was created. Confirm the subscription's exact eligibility and anniversary in Azure Cost Management before applying. Public GHCR container image storage and bandwidth are currently free under GitHub's published Container Registry billing policy.

## Cost controls

- All Container Apps use `min_replicas = 0` and `max_replicas = 1`.
- Message consumers have managed-identity Service Bus scale rules so queued work wakes them from zero.
- Bot maintenance is a manual job, not an always-on worker.
- Log Analytics ingestion is capped at 0.1 GB/day.
- Chargeable log-query alerts are disabled; the retained platform-metric alerts stay within the first ten free time series.
- Bootstrap creates a USD 1 monthly budget alert by default.
- Foundation uses PostgreSQL B1ms, Service Bus Standard, SignalR Free, APIM Consumption, and App Configuration Free. It intentionally provisions no ACR, Key Vault, or Notification Hubs resource.

Azure budgets are notifications, not hard spending stops. Public traffic can consume free grants, so review Cost Analysis and the Free Services blade after deployment and weekly while the demo is public.

## Deployment lifecycle

1. Apply bootstrap as the subscription owner and publish the resulting non-secret GitHub variables.
2. Apply the foundation stack and verify every planned SKU against this document.
3. Run the application deployment workflow. It builds images, applies runtime, migrates databases, runs bot maintenance, validates five Saga scenarios, and updates Vercel.
4. Verify the Azure Free Services blade shows the PostgreSQL and Service Bus meters consuming included quantities.
5. Before the 12-month anniversary, destroy or migrate PostgreSQL and Service Bus. Leaving them running after the anniversary converts them to normal pay-as-you-go billing.

The default Azure hostname and the Vercel domain avoid DNS and certificate charges. Do not add NAT Gateway, private endpoints, dedicated Container Apps profiles, PostgreSQL HA, paid SignalR/APIM tiers, or an Azure registry to this profile.
