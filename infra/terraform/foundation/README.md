# Foundation Terraform

This stack creates the shared Azure services used by the POC runtime:

- ACR Basic
- PostgreSQL Flexible Server with four service-owned databases
- Service Bus Standard topic/subscriptions
- Azure SignalR Free
- Key Vault
- App Configuration Free
- Log Analytics and Application Insights
- Container Apps environment
- APIM Consumption
- Notification Hubs Free

Initialize with the backend created by bootstrap:

```bash
terraform init \
  -backend-config="resource_group_name=$TFSTATE_RESOURCE_GROUP" \
  -backend-config="storage_account_name=$TFSTATE_STORAGE_ACCOUNT" \
  -backend-config="container_name=$TFSTATE_CONTAINER" \
  -backend-config="key=foundation-poc.tfstate" \
  -backend-config="use_azuread_auth=true"
```

Then apply:

```bash
terraform apply \
  -var="tfstate_resource_group_name=$TFSTATE_RESOURCE_GROUP" \
  -var="tfstate_storage_account_name=$TFSTATE_STORAGE_ACCOUNT" \
  -var="tfstate_container_name=$TFSTATE_CONTAINER" \
  -var="publisher_email=you@example.com"
```

After foundation, run `scripts/cloud/postgres-bootstrap.sh` and
`scripts/cloud/remove-servicebus-default-rules.sh`.
