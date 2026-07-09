# GitHub, Terraform, and Azure Full Reset Runbook

This runbook replaces the manual Azure POC and Azure DevOps flow with GitHub
Actions plus Terraform. The destructive steps intentionally discard fictional
demo data. Do not run them against resource groups containing unrelated assets.

## 1. Prerequisites

Install and authenticate:

```bash
az login
az account set --subscription "<subscription-id>"
gh auth login
terraform version
```

Terraform was not installed on this workstation during implementation, so local
Terraform validation could not be executed here. GitHub Actions will run
`terraform fmt -check` and `terraform validate`.

## 2. Inventory the Manual Azure POC

Run from Azure Cloud Shell or an authenticated shell:

```bash
chmod +x scripts/cloud/*.sh
RG=rg-realtime-pix-poc \
TFSTATE_RG=rg-realtime-pix-tfstate \
scripts/cloud/azure-inventory.sh
```

The script writes JSON metadata to `work/azure-inventory/<timestamp>` and never
exports Key Vault secret values.

## 3. Move the Active Repository to GitHub

Run from the authoritative local checkout:

```powershell
.\scripts\cloud\github-migration.ps1
```

It backs up current GitHub `main` to
`archive/pre-terraform-migration-20260702`, then replaces GitHub `main` with
the local `main` using `--force-with-lease`.

Configure the GitHub repository after the first successful CI run:

```bash
OWNER=germanao REPO=realtime-pix-platform scripts/cloud/configure-github-repository.sh
```

Keep Azure DevOps until GitHub CI has passed.

## 4. Destroy the Manual Azure Environment

After inventory review:

```bash
CONFIRM_DELETE=delete-rg-realtime-pix-poc \
RG=rg-realtime-pix-poc \
scripts/cloud/destroy-manual-azure.sh
```

Only if the old state resource group belongs exclusively to this project:

```bash
CONFIRM_DELETE=delete-rg-realtime-pix-poc \
RG=rg-realtime-pix-poc \
TFSTATE_RG=rg-realtime-pix-tfstate \
DELETE_TFSTATE_RG=yes \
scripts/cloud/destroy-manual-azure.sh
```

Verify cleanup:

```bash
az group exists --name rg-realtime-pix-poc
az resource list --tag project=realtime-pix --output table
az keyvault list-deleted --output table
```

## 5. Bootstrap Terraform

Bootstrap creates the Terraform state storage, the app resource group, GitHub
OIDC identity, RBAC, and optional budget.

```bash
cd infra/terraform/bootstrap
terraform init -backend=false
terraform apply \
  -var="github_owner=germanao" \
  -var="github_repository=realtime-pix-platform" \
  -var="github_environment_name=poc" \
  -var="budget_contact_emails=[\"you@example.com\"]"
```

Migrate the bootstrap state into the Azure Blob backend before running
foundation. Foundation reads these bootstrap outputs through
`bootstrap.tfstate`.

```bash
cd ../../..
scripts/cloud/migrate-bootstrap-state.sh
cd infra/terraform/bootstrap
```

Export outputs:

```bash
export AZURE_CLIENT_ID="$(terraform output -raw github_actions_client_id)"
export AZURE_TENANT_ID="$(terraform output -raw tenant_id)"
export AZURE_SUBSCRIPTION_ID="$(terraform output -raw subscription_id)"
export TFSTATE_RESOURCE_GROUP="$(terraform output -raw tfstate_resource_group_name)"
export TFSTATE_STORAGE_ACCOUNT="$(terraform output -raw tfstate_storage_account_name)"
export TFSTATE_CONTAINER="$(terraform output -raw tfstate_container_name)"
```

Set GitHub environment variables:

```bash
OWNER=germanao REPO=realtime-pix-platform ENVIRONMENT=poc \
scripts/cloud/bootstrap-github-variables.sh
```

Create a GitHub environment named `poc`, require approval, then set:

```bash
gh variable set PUBLISHER_EMAIL --repo germanao/realtime-pix-platform --env poc --body "you@example.com"
```

## 6. Foundation Terraform

Foundation provisions ACR, PostgreSQL, Service Bus, SignalR, Key Vault, App
Configuration, observability, Container Apps environment, APIM, and Notification
Hubs.

```bash
cd ../foundation
terraform init \
  -backend-config="resource_group_name=$TFSTATE_RESOURCE_GROUP" \
  -backend-config="storage_account_name=$TFSTATE_STORAGE_ACCOUNT" \
  -backend-config="container_name=$TFSTATE_CONTAINER" \
  -backend-config="key=foundation-poc.tfstate" \
  -backend-config="use_azuread_auth=true"

terraform apply \
  -var="tfstate_resource_group_name=$TFSTATE_RESOURCE_GROUP" \
  -var="tfstate_storage_account_name=$TFSTATE_STORAGE_ACCOUNT" \
  -var="tfstate_container_name=$TFSTATE_CONTAINER" \
  -var="publisher_email=you@example.com"
```

## 7. PostgreSQL Roles and Secrets

Terraform creates the PostgreSQL server and databases. PostgreSQL application
roles are configured by script because they run inside PostgreSQL itself.

```bash
export PGHOST="$(terraform output -raw postgres_fqdn)"
export KEYVAULT_NAME="$(terraform output -raw key_vault_name)"
export PGADMIN_USER="$(terraform output -raw postgres_admin_login)"
scripts/cloud/postgres-bootstrap.sh
```

This creates `identity_app`, `wallet_app`, `transaction_app`, and
`realtime_app`, then updates Key Vault secrets `identity-db`, `wallet-db`,
`transaction-db`, and `realtime-db`.

## 8. Service Bus Default Rule Cleanup

Azure creates a default `TrueFilter` rule. Terraform creates SQL filters, then
this script removes the default rule:

```bash
export RG="$(terraform output -raw resource_group_name)"
export SERVICEBUS_NAMESPACE="$(terraform output -raw servicebus_namespace_name)"
scripts/cloud/remove-servicebus-default-rules.sh
```

## 9. Build Images

GitHub Actions builds images with Docker and pushes them to ACR. Manual
equivalent:

```bash
ACR_NAME="$(terraform output -raw acr_name)"
ACR_LOGIN_SERVER="$(terraform output -raw acr_login_server)"
TAG="$(git rev-parse --short HEAD)"

az acr login --name "$ACR_NAME"

docker build --file services/api-gateway/Dockerfile --tag "$ACR_LOGIN_SERVER/realtime-pix/api-gateway:$TAG" .
docker push "$ACR_LOGIN_SERVER/realtime-pix/api-gateway:$TAG"

docker build --file services/identity-presence-service/Dockerfile --tag "$ACR_LOGIN_SERVER/realtime-pix/identity-presence-service:$TAG" .
docker push "$ACR_LOGIN_SERVER/realtime-pix/identity-presence-service:$TAG"

docker build --file services/wallet-ledger-service/Dockerfile --tag "$ACR_LOGIN_SERVER/realtime-pix/wallet-ledger-service:$TAG" .
docker push "$ACR_LOGIN_SERVER/realtime-pix/wallet-ledger-service:$TAG"

docker build --file services/transaction-service/Dockerfile --tag "$ACR_LOGIN_SERVER/realtime-pix/transaction-service:$TAG" .
docker push "$ACR_LOGIN_SERVER/realtime-pix/transaction-service:$TAG"

docker build --file services/realtime-events-service/Dockerfile --tag "$ACR_LOGIN_SERVER/realtime-pix/realtime-events-service:$TAG" .
docker push "$ACR_LOGIN_SERVER/realtime-pix/realtime-events-service:$TAG"

docker build --file services/bot-service/Dockerfile --tag "$ACR_LOGIN_SERVER/realtime-pix/bot-service:$TAG" .
docker push "$ACR_LOGIN_SERVER/realtime-pix/bot-service:$TAG"
```

## 10. Runtime Terraform

Runtime deploys the six Container Apps, managed identities/RBAC, public
endpoints, and APIM health route.

```bash
cd ../runtime
terraform init \
  -backend-config="resource_group_name=$TFSTATE_RESOURCE_GROUP" \
  -backend-config="storage_account_name=$TFSTATE_STORAGE_ACCOUNT" \
  -backend-config="container_name=$TFSTATE_CONTAINER" \
  -backend-config="key=runtime-poc.tfstate" \
  -backend-config="use_azuread_auth=true"

terraform apply \
  -var="tfstate_resource_group_name=$TFSTATE_RESOURCE_GROUP" \
  -var="tfstate_storage_account_name=$TFSTATE_STORAGE_ACCOUNT" \
  -var="tfstate_container_name=$TFSTATE_CONTAINER" \
  -var="image_tag=$TAG"
```

## 11. Vercel Configuration

The existing `realtime-pix-web` project is configured separately from backend
Terraform. After runtime applies, read the public URLs:

```bash
cd infra/terraform/runtime
terraform output -raw apim_api_url
terraform output -raw presence_hub_url
terraform output -raw events_hub_url
```

Set these public variables in Vercel:

- `NEXT_PUBLIC_API_BASE_URL`
- `NEXT_PUBLIC_PRESENCE_HUB_URL`
- `NEXT_PUBLIC_EVENTS_HUB_URL`

## 12. GitHub Actions

Use:

- `ci.yml` for backend, frontend, Dockerfile, and Terraform static checks,
- `terraform-plan.yml` for static validation and manual trusted plans,
- `deploy-poc.yml` for foundation, ACR builds, runtime, and smoke,
- `destroy-poc.yml` for guarded runtime/foundation destruction.

First deployment:

```bash
gh workflow run deploy-poc.yml \
  --repo germanao/realtime-pix-platform \
  -f apply_runtime=true
```

## 13. Validation

```bash
API_URL="$(terraform output -raw apim_api_url)"
curl --fail "$API_URL/health"
curl --fail "$API_URL/presence/users"
```

Browser validation:

- open two browsers or a normal window plus a private window,
- a new anonymous user should appear immediately in the other session,
- closing one session should remove it quickly,
- one transfer click should produce one transfer, one debit, one credit, and
  one completion event,
- the event timeline and transfer flow should update through SignalR.

## 14. Application Status

Implemented now:

- real Service Bus event bus provider,
- Azure SignalR activation by connection string,
- Container Apps health endpoints,
- Terraform bootstrap/foundation/runtime topology,
- GitHub Actions CI, plan, deploy, and destroy workflows,
- reset and bootstrap scripts.

Still required before a production-style claim:

- replace in-memory wallet, transaction, identity, and realtime stores with
  PostgreSQL/EF Core repositories,
- add transactional outbox dispatchers for database-backed services,
- load Azure App Configuration at process startup,
- emit OpenTelemetry traces to Application Insights,
- add cloud integration tests for Service Bus and PostgreSQL.
