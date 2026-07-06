# Security Policy

This repository is intended to be public and safe to clone for local demos.

## Secret Handling

- Do not commit `.env`, Terraform state, `.tfvars`, local event-bus files, test
  outputs, database dumps, Key Vault exports, or cloud credentials.
- Use GitHub Actions OIDC for Azure authentication. Do not create or commit an
  Azure client secret for CI/CD.
- Store runtime secrets in Azure Key Vault or protected GitHub environment
  secrets.
- Values prefixed with `NEXT_PUBLIC_` are browser-visible and must never contain
  secrets.
- Deployment scripts may be public when they read secrets from prompts, Azure Key
  Vault, or GitHub secrets at runtime.

## Reporting

If you find a credential, private endpoint, or sensitive operational value in
the repository, rotate it immediately and open an issue describing the affected
file and commit range without pasting the secret value.
