# Azure Provisioning Model

The backend is Azure-only; the Next.js frontend uses Vercel. Terraform is the source of truth after the initial bootstrap.

## POC Profile

| Capability | Azure resource |
| --- | --- |
| Containers | Container Apps Consumption |
| HTTP edge | API Management Consumption |
| Images | Container Registry Basic |
| Messaging | Service Bus Standard |
| Browser realtime | SignalR Free |
| Relational state | PostgreSQL Flexible Server B1ms |
| Configuration | App Configuration Free |
| Secrets/bootstrap | Key Vault Standard |
| Telemetry | Log Analytics and Application Insights |
| Future push | Notification Hubs Free |

Five databases are active: `identity_presence_db`, `bank_a_ledger_db`, `bank_b_ledger_db`, `transaction_db`, and `realtime_projection_db`. `wallet_ledger_db` is a trafficless one-release rollback artifact, not an active ownership boundary.

All seven active Container Apps use a dedicated user-assigned managed identity and `min_replicas = 1`. Bank A and Bank B have separate databases, queues, and identities even though they use one image. PostgreSQL authentication and Azure SDK access use Microsoft Entra tokens; no application database password or Service Bus shared key is stored in Container Apps.

Dynamic Vercel preview hosts require one explicit POC compromise. Azure SignalR accepts exact origins or the global `*`, but not partial host wildcards, so its managed transport uses `*` in the POC. The Identity/Presence and Realtime negotiate endpoints still allow only localhost, the production Vercel URL, and generated preview hosts containing both the configured Vercel project name (`realtime-pix`) and owning scope (`germanaos-projects`); APIM applies the same constraint. Forks must override both values. The non-deployed production profile uses exact SignalR origins. See the [Azure SignalR CORS CLI contract](https://learn.microsoft.com/en-us/cli/azure/signalr/cors?view=azure-cli-latest).

## State Separation

| Stack | State key | Lifecycle |
| --- | --- | --- |
| Bootstrap | `bootstrap.tfstate` | State storage, resource group, budget, GitHub OIDC identities |
| Foundation | `poc/foundation.tfstate` | Shared Azure PaaS resources and message topology |
| Runtime | `poc/runtime.tfstate` | Identities, Container Apps, APIM API/policies, alerts |

Bootstrap is protected from routine destroy. Runtime can be replaced without recreating data services. Foundation changes require explicit environment approval.

Bootstrap is also excluded from GitHub apply workflows. A subscription owner runs it from an authenticated local shell, reviews the plan, applies it, and then publishes only its non-secret outputs as GitHub variables. This avoids giving an automation identity permission to create or expand its own trust and RBAC assignments.

## GitHub Identity Boundaries

- Plan identity: read-only Azure access for trusted pull requests.
- Image identity: ACR push only.
- Apply identity: scoped management of the application resource group plus state data-plane access; it has no bootstrap, state-resource-group, or subscription-budget permissions.

Federated credentials bind exact GitHub repository/environment subjects. Workflows store only client, tenant, subscription, and backend identifiers; Azure client secrets are not used.

## Production Reference

`infra/terraform/production-reference` is plan-valid documentation, not an automatic deployment target. It uses:

- Separate private PostgreSQL servers per state owner.
- Service Bus Premium and ACR Premium private endpoints.
- A VNet-integrated Container Apps workload-profile environment.
- Seven dedicated workload identities and seven Container Apps on a bounded D4 profile.
- APIM Standard v2 outbound VNet integration.
- APIM routes to the internal Gateway and exposes only Identity/Realtime SignalR negotiation; SignalR Standard keeps public browser client connections while server/REST paths use its private endpoint.
- Private DNS, purge-protected Key Vault, HA, and longer retention.

It is a reviewed starting point, not a universal production template. Real use still requires organization-specific SLOs, threat modeling, egress, disaster recovery, data residency, and capacity decisions. It also requires a private-network migration runner to create the five database principals and apply EF migration bundles before enabling traffic.

## Apply Paths

- Application release: `.github/workflows/deploy-poc.yml`
- Explicit infrastructure apply: `.github/workflows/infrastructure-apply.yml`
- Pull-request plan/security checks: `.github/workflows/terraform-plan.yml`
- Scheduled drift detection: `.github/workflows/terraform-drift.yml`
- Manual destroy: `.github/workflows/destroy-poc.yml`

See [the deployment index](../deployment/README.md) and the README in each Terraform root/module.
