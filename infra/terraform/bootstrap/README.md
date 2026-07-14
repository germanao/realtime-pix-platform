# Bootstrap Terraform

This stack is an owner-operated trust bootstrap. It creates the Azure Blob backend, application resource group, budget, and least-privilege GitHub OIDC identities used by the other stacks. GitHub Actions deliberately cannot plan or apply this stack because an automation identity must not create or expand its own permissions.

```bash
cd infra/terraform/bootstrap
terraform init -backend=false
terraform apply
```

After apply, migrate bootstrap itself to the Azure backend:

```bash
cd ../../..
scripts/cloud/migrate-bootstrap-state.sh
```

For an existing remote state, initialize it with the backend identifiers emitted by the original bootstrap apply, then review and apply changes while authenticated as the subscription owner:

```bash
terraform init -reconfigure \
  -backend-config="resource_group_name=$TFSTATE_RESOURCE_GROUP" \
  -backend-config="storage_account_name=$TFSTATE_STORAGE_ACCOUNT" \
  -backend-config="container_name=$TFSTATE_CONTAINER" \
  -backend-config="key=bootstrap.tfstate" \
  -backend-config="use_azuread_auth=true"
terraform plan -out=bootstrap.tfplan
terraform apply bootstrap.tfplan
rm -f bootstrap.tfplan
```

Do not commit or upload the binary plan. Set the resulting non-secret GitHub variables with `scripts/cloud/bootstrap-github-variables.sh`.

Bootstrap creates distinct GitHub OIDC identities for read-only plans, ACR image pushes, and approved foundation/runtime applies. Those identities are scoped to the state data plane and application resource group; they do not manage the state resource group, subscription budget, or bootstrap identity definitions.
