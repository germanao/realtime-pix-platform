# Bootstrap Terraform

This stack is run locally first because it creates the Azure Blob backend used by the other stacks.

```bash
cd infra/terraform/bootstrap
terraform init -backend=false
terraform apply
```

After apply, migrate bootstrap itself to the Azure backend:

```bash
terraform init -migrate-state \
  -force-copy \
  -backend-config="resource_group_name=$(terraform output -raw tfstate_resource_group_name)" \
  -backend-config="storage_account_name=$(terraform output -raw tfstate_storage_account_name)" \
  -backend-config="container_name=$(terraform output -raw tfstate_container_name)" \
  -backend-config="key=bootstrap.tfstate" \
  -backend-config="use_azuread_auth=true"
```

Set the GitHub environment variables with `scripts/cloud/bootstrap-github-variables.sh`.
