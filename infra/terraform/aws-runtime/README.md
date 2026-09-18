# AWS on-demand demo runtime

This stack runs the ten-user backend on one EC2 instance in `us-east-2`, behind CloudFront. Vercel serves the frontend and GHCR stores the application images.

## Components

| Capability | Implementation |
| --- | --- |
| Service compute | Docker Compose on encrypted EC2/EBS |
| Relational state | PostgreSQL 16 with separate service databases |
| Messaging | Durable PostgreSQL queues/topics plus transactional outbox/inbox |
| Realtime | Direct ASP.NET Core SignalR through nginx and CloudFront |
| Wake and limits | Lambda, EventBridge, and DynamoDB |
| Configuration | SSM SecureString `/realtime-pix/poc/aws-only-env` |
| Images | Commit-pinned GHCR tags |
| Recovery | Private encrypted S3 backups |

The single-host design is intentionally basic and not highly available. Do not add replicas without designing distributed delivery claims and a SignalR backplane.

## Runtime behavior

The browser calls `/runtime/wake`. The controller starts exactly the Terraform-managed instance and waits for service readiness. Browser activity extends the session; a five-minute controller check stops the host after 20 idle minutes and enforces six aggregate running hours per UTC day. There is no scheduled weekday start and no fallback runtime.

The allowance controls EC2 runtime, not total billing. EBS, public IPv4, S3, CloudFront, Lambda, DynamoDB, logs, and snapshots may incur charges while the instance is stopped. The AWS budget is an alert, not a hard cutoff.

## Terraform

The S3 backend uses partial configuration so no account-specific bucket is committed:

```powershell
terraform -chdir=infra/terraform/aws-runtime init -backend-config="bucket=realtime-pix-tfstate-<account-id>"
terraform -chdir=infra/terraform/aws-runtime plan -var="budget_email=<email>"
```

Review every plan. The EC2 root volume has `delete_on_termination = false`; AMI and bootstrap changes are intentionally ignored to prevent an accidental data-host replacement. Host changes are delivered through SSM.

## Deploy an application release

1. Wait for `publish-aws-runtime.yml` to publish `aws-<commit-sha>` images.
2. Set `RUNTIME_IMAGE_TAG` in the protected SSM environment to that exact tag.
3. Run `update-host.ps1 -InstanceId <id>` from an authenticated AWS CLI session.
4. Verify `/health/ready`, direct SignalR, successful and compensated transfers, presence expiry, idle shutdown, and a stopped-host cold start.

Never deploy `aws-latest` as the source of truth.

## Backups and recovery

`backup-databases.sh` dumps the five active service databases and the durable event-bus database, then uploads private AES-256-encrypted objects to the current account's `realtime-pix-tfstate-<account-id>` bucket. The runtime role can write the backup prefix but cannot delete objects. Database ports are not published.

Install or refresh the timer with `scripts/cloud/install-aws-backups.ps1 -InstanceId <id>`. Restore into fresh PostgreSQL 16 databases using `pg_restore --no-owner --no-privileges`, provision the SSM environment, and match image versions to schema versions before starting traffic. Never use `docker compose down -v` or remove `/opt/realtime-pix/data/postgres` without a verified off-host backup.
