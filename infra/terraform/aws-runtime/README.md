# AWS-only demo runtime

The ten-user demo runs on the existing `c7i-flex.large` in `us-east-2`, behind CloudFront. Vercel serves the frontend and GitHub/GHCR builds and stores the application images. **No running service requires Azure.**

## Dependency mapping

| Former Azure dependency | AWS-hosted replacement |
| --- | --- |
| PostgreSQL Flexible Server | PostgreSQL 16 container; separate service databases/users on encrypted EBS |
| Service Bus | Durable PostgreSQL event/command transport with independent consumer acknowledgements and the existing transactional outbox/inbox |
| Azure SignalR | Direct ASP.NET Core SignalR through nginx and CloudFront; one events connection per browser |
| Container Apps / API Management | Existing EC2 Docker services and nginx |
| App Configuration / workload credentials | Private SSM SecureString and explicit Docker environment settings |
| Application Insights / Log Analytics | Size-limited container logs; controller logs in CloudWatch |
| Bot Container Apps job | Small bot worker on the same host |

The single-host design is intentionally basic, not highly available. Do not scale a consumer to multiple replicas without adding distributed delivery claims. Message delivery is at least once; inbox deduplication and idempotent domain handlers remain essential. Acknowledgements are per message, not a numeric cursor, so concurrent commits cannot skip messages.

## On-demand operation and costs

The browser POSTs `/runtime/wake`; Lambda starts exactly this existing instance. It waits for dependency readiness before joining. Cached container images are used at boot. Activity heartbeats keep the host awake; the controller stops it after 20 idle minutes. Six aggregate running hours are allowed per UTC day, enforced every five minutes. There is no weekday restriction. When the allowance is exhausted, the UI shows a clear notice until 00:00 UTC; there is **no Azure fallback**.

This consumes AWS credits and is not a zero-charge guarantee. Disk, retained public IP, snapshots/backups, CloudFront, controller and logs can incur charges even while EC2 is stopped. The $15 monthly budget is an alert, not a billing cutoff. Anonymous visitors can consume the shared allowance. The private origin header and instance role never reach the browser.

## Data safety and migration

`migrate-to-aws.py` is a guarded, one-time operation invoked by `scripts/cloud/deploy-aws-only.ps1`. It pulls pinned images before pausing writers, dumps the five active service databases plus the retired wallet database, uploads private encrypted archives, restores with independent passwords, and compares exact public-table row counts. It replays historical outbox envelopes into the new transport while preserving the previous inbox consumer keys, so already handled events are deduplicated and stranded published messages can recover.

The script refuses to overwrite a populated destination or repeat a completed migration. It does not delete Azure. Any interruption before the completion marker requires inspection, not a forced rerun. Old configuration stays in `/opt/realtime-pix/migration-aws-only` until cloud cleanup is verified.

Backups are private objects under `s3://realtime-pix-tfstate-886781461608/backups/aws-only/`. The runtime role can write only that prefix and cannot delete backups. `backup-databases.sh` exports the seven databases, including the durable bus, for scheduled off-host recovery. Database ports are not published. The encrypted root EBS volume is retained if the instance is terminated. Normal stops/reboots preserve the database directory.

Restore archives into fresh databases using PostgreSQL 16 `pg_restore --no-owner --no-privileges`, recreate the restricted database roles, and provision `/realtime-pix/poc/aws-only-env` as a SecureString before starting applications. Match image versions to schema versions. Never run `docker compose down -v`, delete `data/postgres`, or rebuild a host without preserving its volume and verified backups.

## Deployment and verification

`publish-aws-runtime.yml` has no Azure authentication step. It publishes `aws-<commit SHA>` and `aws-latest` images, including the bot. Production compose pins `RUNTIME_IMAGE_TAG` to a tested commit. Pull explicitly during deployment, recreate the affected containers, restart nginx to refresh upstream IPs, and save the updated environment in SSM. `user_data` is ignored for the existing instance to avoid replacement; bootstrap changes are rolled out through SSM.

Before release: run web tests/build, .NET tests, Docker-backed PostgreSQL transport tests, Terraform validation and wake-controller tests. In production verify ten concurrent clients, direct SignalR, successful and compensated transfers, presence expiry, and a stopped-host cold start. Before deleting Azure, verify copied row counts, private off-host archives, and the AWS-only application. Keep the retired Azure GitHub deployment/drift workflows disabled so they cannot recreate resources.
