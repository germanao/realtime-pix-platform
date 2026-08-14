# Foundation Terraform

This stack creates only the long-lived services used by the POC: one Entra-enabled PostgreSQL server, Service Bus Standard, SignalR Free, App Configuration Free, capped observability, a Container Apps Consumption environment, and APIM Consumption. Public application images live in GitHub Container Registry, so the POC does not provision ACR. Unused Key Vault and Notification Hubs resources are intentionally omitted.

Five databases are active (`identity_presence_db`, both bank ledger databases, `transaction_db`, and `realtime_projection_db`). `wallet_ledger_db` and its `1 = 0` subscription are retained trafficless for one rollback release.

Initialize with the bootstrap backend:

```bash
terraform init \
  -backend-config="resource_group_name=$TFSTATE_RESOURCE_GROUP" \
  -backend-config="storage_account_name=$TFSTATE_STORAGE_ACCOUNT" \
  -backend-config="container_name=$TFSTATE_CONTAINER" \
  -backend-config="key=poc/foundation.tfstate" \
  -backend-config="use_azuread_auth=true"
```

Use `.github/workflows/infrastructure-apply.yml` for approved applies. For a local reviewed plan:

```bash
terraform plan \
  -var-file=../environments/poc/foundation.tfvars \
  -var="tfstate_resource_group_name=$TFSTATE_RESOURCE_GROUP" \
  -var="tfstate_storage_account_name=$TFSTATE_STORAGE_ACCOUNT" \
  -var="tfstate_container_name=$TFSTATE_CONTAINER" \
  -var="publisher_email=you@example.com"
```

Database principals, EF migrations, and removal of Azure-created default Service Bus rules are data-plane deployment steps handled by `deploy-poc.yml` after a temporary runner firewall rule is opened.
