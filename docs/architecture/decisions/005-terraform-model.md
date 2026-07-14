# ADR-005: Separate Terraform Stacks and Profiles

**Status:** Accepted

## Context

State infrastructure, long-lived data services, and frequently changing application revisions have different risk and lifecycle. A single state would increase blast radius.

## Decision

Use bootstrap, foundation, and runtime roots with separate Azure Blob keys. Compose typed modules for repeated resources. Track a low-cost POC profile and a non-deployed production reference. Use moved blocks for refactors and GitHub OIDC identities with separate plan, image, and apply permissions. Bootstrap remains an owner-operated local action and is excluded from GitHub apply workflows so automation cannot grant or expand its own trust.

## Consequences

- Routine releases do not recreate databases or state storage.
- Cross-stack outputs are explicit, but apply order matters.
- Modules reduce duplication without pretending every environment is identical.
- Bootstrap changes require an authenticated subscription owner and a reviewed local plan.
- Production reference must be reviewed and adapted before real use; it is not an automatic best-practice certificate.
