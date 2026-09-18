# ADR-005: Keep One Explicit Demo Infrastructure Stack

**Status:** Accepted

## Context

The showcase has one supported AWS topology. Keeping retired cloud stacks beside it makes reviews ambiguous and can accidentally recreate billable resources.

## Decision

Keep one Terraform root for the on-demand AWS runtime. Use a private S3 backend configured at `terraform init`, keep application releases separate from infrastructure changes, and require a reviewed plan before apply. Publish application images to GHCR and update the host through SSM.

## Consequences

- Routine releases do not recreate databases or state storage.
- The repository has one unambiguous, testable deployment path.
- Backend bucket configuration remains account-specific and outside source control.
- This demo stack is not a production reference architecture.
