# Runtime Terraform

This stack deploys the six Container Apps and public Azure endpoints. It expects
images to already exist in ACR with the tag passed by `image_tag`.

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

The frontend is hosted separately in Vercel. After this stack applies, use the
runtime outputs to configure these Vercel environment variables:

- `NEXT_PUBLIC_API_BASE_URL` from `terraform output -raw apim_api_url`
- `NEXT_PUBLIC_PRESENCE_HUB_URL` from `terraform output -raw presence_hub_url`
- `NEXT_PUBLIC_EVENTS_HUB_URL` from `terraform output -raw events_hub_url`
