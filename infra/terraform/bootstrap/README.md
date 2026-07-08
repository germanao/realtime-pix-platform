# Bootstrap Terraform

This stack is run locally first because it creates the Azure Blob backend used by the other stacks.

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

Set the GitHub environment variables with `scripts/cloud/bootstrap-github-variables.sh`.
