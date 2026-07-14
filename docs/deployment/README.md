# Deployment Guide Index

Terraform and GitHub Actions are the current deployment path. The Azure Portal/Azure DevOps documents remain as learning history and must not be used as the source of truth for the current Saga topology.

## Current Path

1. Apply `infra/terraform/bootstrap` locally once for a new Azure environment.
2. Migrate bootstrap state with `scripts/cloud/migrate-bootstrap-state.sh`.
3. For an existing environment, run `infrastructure-apply.yml` with operation `plan`, stack `bootstrap`, and confirmation `plan-poc`; review the plan before running the matching `apply`/`apply-poc` operation.
4. Run `scripts/cloud/migrate-environment-state-keys.sh` once if old POC state keys exist.
5. Bootstrap apply synchronizes the non-secret GitHub variables. The local alternative is `scripts/cloud/bootstrap-github-variables.sh`.
6. Run `infrastructure-apply.yml` with operation `plan` before an approved `apply` for foundation changes.
7. Run `deploy-poc.yml` to build immutable images, apply runtime, configure Entra database principals, run EF migrations, execute cloud Saga smoke tests, and synchronize Vercel variables.

The deployment workflow performs five Saga scenarios through APIM: completion, debit rejection, compensated credit rejection, compensated credit timeout, and refund rejection/manual intervention. It verifies persisted transitions, unique ledger operations, projections, idempotent replay, and fictional-money accounting.

## Required GitHub Configuration

GitHub environment `poc` requires:

- `AZURE_APPLY_CLIENT_ID` (with `AZURE_CLIENT_ID` retained as a temporary compatibility alias)
- `AZURE_IMAGE_CLIENT_ID`
- `AZURE_TENANT_ID`
- `AZURE_SUBSCRIPTION_ID`
- `TFSTATE_RESOURCE_GROUP`
- `TFSTATE_STORAGE_ACCOUNT`
- `TFSTATE_CONTAINER`
- `PUBLISHER_EMAIL`
- secret `VERCEL_API_TOKEN`

Trusted pull-request plans require repository-scoped `AZURE_PLAN_CLIENT_ID`, tenant/subscription identifiers, Terraform backend identifiers, and `PUBLISHER_EMAIL`. Bootstrap apply or the helper script writes these non-secret values; the plan identity has Reader and Blob Data Reader roles only.

Use exact names emitted by Terraform outputs and `scripts/cloud/bootstrap-github-variables.sh`; never commit values.

## Operational Rules

- Infrastructure apply and destroy require the protected `poc` environment.
- Deployment concurrency queues; it does not cancel a state-writing run.
- Database firewall access is temporary and removed in unconditional cleanup.
- EF migrations run before revisions are restarted.
- Foundation and runtime use different state keys.
- Binary Terraform plans are not uploaded from this public repository.
- Production reference is never applied by a repository workflow.

## Historical Guides

These preserve the learning sequence that created the original six-service POC. Names, SKUs, database counts, state keys, and commands can be obsolete:

- [Full reset migration](github-terraform-full-reset.md)
- [Manual Azure provisioning](recommended-cloud-services-provisioning.md)
- [Old cloud integration specification](cloud-integration-change-specification.md)
- [Azure Portal Container Apps POC](azure-container-apps-portal-poc.md)
- [Original Terraform/CI/CD guide](azure-container-apps-terraform-cicd.md)
- [Original scripted steps 16-26](azure-scripts-steps-16-26.md)

For current behavior use [the architecture guide](../architecture/README.md), [Azure provisioning model](../architecture/cloud-provisioning.md), Terraform code, and workflow code.
