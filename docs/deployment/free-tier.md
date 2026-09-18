# Demo cost controls

The frontend uses Vercel Hobby. The backend uses AWS promotional credits and low-volume services; it is not guaranteed to remain at zero cost.

| Capability | Current service | Guardrail |
| --- | --- | --- |
| Frontend | Vercel Hobby | Static/Next.js production deployment |
| Compute | One EC2 instance | Starts on demand, stops after 20 idle minutes, six-hour UTC daily cap |
| Edge | CloudFront | One public runtime origin and low demo traffic |
| Runtime control | Lambda, EventBridge, DynamoDB | Five-minute enforcement and minimal state |
| Data | PostgreSQL 16 on encrypted EBS | Private Docker network, single host |
| Images | Public GHCR | Commit-pinned deployment tags |
| Backups/state | Encrypted S3 | Private bucket, restricted write-only backup prefix |
| Configuration | SSM Parameter Store | One SecureString runtime environment |
| Alerts | AWS Budgets | USD 15 monthly notification |

Budgets notify; they do not stop billing. EBS, backups, public IPv4, CloudFront, logs, Lambda, and snapshots can cost money while EC2 is stopped. Promotional credits expire. Review AWS Cost Explorer and the credit balance regularly, and destroy the stack when the showcase is no longer needed.

The six-hour allowance is shared by all visitors. The controller resets it at 00:00 UTC and the frontend reports when the daily allowance is exhausted.
