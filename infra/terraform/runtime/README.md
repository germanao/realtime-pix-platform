# Runtime Terraform

This stack deploys seven active workloads: Gateway, Identity/Presence, Bank A, Bank B, Transaction, Realtime Events, and Bot. The bank deployments share an image but use separate identity/database/queue configuration. A trafficless legacy wallet app remains at zero replicas for one release.

Images must already exist in ACR with the immutable `image_tag` supplied by the deployment workflow.

```bash
terraform init \
  -backend-config="resource_group_name=$TFSTATE_RESOURCE_GROUP" \
  -backend-config="storage_account_name=$TFSTATE_STORAGE_ACCOUNT" \
  -backend-config="container_name=$TFSTATE_CONTAINER" \
  -backend-config="key=poc/runtime.tfstate" \
  -backend-config="use_azuread_auth=true"

terraform plan \
  -var-file=../environments/poc/runtime.tfvars \
  -var="tfstate_resource_group_name=$TFSTATE_RESOURCE_GROUP" \
  -var="tfstate_storage_account_name=$TFSTATE_STORAGE_ACCOUNT" \
  -var="tfstate_container_name=$TFSTATE_CONTAINER" \
  -var="image_tag=$(git rev-parse --short HEAD)"
```

Normal application releases use `.github/workflows/deploy-poc.yml`, not a local apply. Vercel receives only:

- `NEXT_PUBLIC_API_BASE_URL` from `apim_api_url`
- `NEXT_PUBLIC_PRESENCE_HUB_URL` from `presence_hub_url`
- `NEXT_PUBLIC_EVENTS_HUB_URL` from `events_hub_url`

Preview CORS is restricted to generated hosts containing both the configured
Vercel project name (`realtime-pix`) and owning scope (`germanaos-projects`).
Override `vercel_preview_project_name` and `vercel_preview_scope_slug` when
forking the repository into a different Vercel account.

No backend secret is exposed through a `NEXT_PUBLIC_` variable.
