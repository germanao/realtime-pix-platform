# Deployment

The supported deployment is Vercel plus the on-demand AWS runtime in `us-east-2`.

## Release paths

- `.github/workflows/deploy-web.yml` publishes `apps/web` to the production Vercel project.
- `.github/workflows/publish-aws-runtime.yml` publishes immutable service images to GHCR.
- `infra/terraform/aws-runtime/update-host.ps1` updates the existing AWS host through SSM.
- `infra/terraform/aws-runtime` owns CloudFront, the wake controller, runtime guardrails, IAM, backups, and the existing EC2 integration.

The normal release order is: merge a green pull request, wait for image publishing, update the host to the tested commit image tag, verify readiness and a complete PIX flow, then verify idle shutdown. Infrastructure changes require a reviewed Terraform plan before apply.

## Required external configuration

- GitHub environment `poc`: `VERCEL_API_TOKEN`.
- Vercel production variable: `NEXT_PUBLIC_AWS_RUNTIME_URL`.
- AWS SSM SecureString: `/realtime-pix/poc/aws-only-env`.
- GHCR images readable by the runtime host.

No Azure credentials or resources are required.

## Operational guardrails

- The browser wake endpoint starts only the existing instance.
- The controller enforces an aggregate six-hour daily allowance and stops the host after 20 idle minutes.
- Database ports are private to Docker networking.
- Runtime secrets stay in SSM and are never embedded in the frontend or repository.
- Production images are pinned to a commit tag; `aws-latest` is a convenience tag, not the deployment source of truth.
- Backups are encrypted, private, off-host objects and the runtime role cannot delete them.

For procedures and recovery notes, use the [AWS runtime guide](../../infra/terraform/aws-runtime/README.md). For expected spend and limits, see [cost controls](free-tier.md).
