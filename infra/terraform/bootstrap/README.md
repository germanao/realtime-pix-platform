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

Bootstrap creates distinct GitHub OIDC identities for read-only plans, ACR image pushes, and approved applies. For an existing remote bootstrap state, `infrastructure-apply.yml` supports a protected `bootstrap` plan followed by an explicitly confirmed apply; the apply synchronizes the resulting non-secret GitHub variables. A fresh environment still bootstraps locally because no Azure OIDC identity exists yet.
