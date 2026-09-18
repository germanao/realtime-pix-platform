# Security policy

This repository is public and intended for a synthetic-money demonstration only.

## Secret handling

- Never commit `.env` files, Terraform state, `.tfvars`, database dumps, private keys, cloud credentials, origin-header values, or SSM exports.
- Store runtime secrets in AWS Systems Manager Parameter Store and CI secrets in protected GitHub environments.
- Use short-lived or workload credentials for automation; do not create long-lived cloud keys for GitHub Actions.
- Treat every `NEXT_PUBLIC_` value as browser-visible. It must never contain a secret.
- Deploy commit-pinned images and keep production configuration out of container images.
- Rotate any exposed credential immediately, then remove it from both the current tree and Git history as appropriate.

## Supported security boundary

The live environment is a ten-user showcase with anonymous synthetic identities and fictional balances. It is not approved for personal data, real financial information, real PIX transactions, regulated workloads, or production availability requirements.

## Reporting

Report vulnerabilities privately through GitHub's security-advisory feature. Include affected versions and reproduction steps, but do not paste live credentials or sensitive operational values into a public issue.
