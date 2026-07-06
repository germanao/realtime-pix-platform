# Runtime Terraform

This stack deploys the six Container Apps and public endpoints. It expects images
to already exist in ACR with the tag passed by `image_tag`.

Initialize:

```bash
terraform init \
  -backend-config="resource_group_name=$TFSTATE_RESOURCE_GROUP" \
  -backend-config="storage_account_name=$TFSTATE_STORAGE_ACCOUNT" \
  -backend-config="container_name=$TFSTATE_CONTAINER" \
  -backend-config="key=runtime-poc.tfstate" \
  -backend-config="use_azuread_auth=true"
```

Apply:

```bash
terraform apply \
  -var="tfstate_resource_group_name=$TFSTATE_RESOURCE_GROUP" \
  -var="tfstate_storage_account_name=$TFSTATE_STORAGE_ACCOUNT" \
  -var="tfstate_container_name=$TFSTATE_CONTAINER" \
  -var="image_tag=$(git rev-parse --short HEAD)"
```

Vercel is disabled by default because an existing project must be imported into
state once:

```bash
terraform import \
  -var="manage_vercel=true" \
  -var="vercel_api_token=$VERCEL_API_TOKEN" \
  'vercel_project.web[0]' \
  prj_xxxxxxxxxxxxxxxxxxxxxxxxxxxx
```

After import, re-run `terraform apply -var="manage_vercel=true" ...`.
