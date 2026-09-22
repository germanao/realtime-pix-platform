# AWS provisioning model

Terraform in `infra/terraform/aws-runtime` is the only supported cloud infrastructure definition. It integrates an existing EC2 host with the public on-demand controller and supporting AWS services in `us-east-2`.

| Capability | Resource |
| --- | --- |
| Public edge | CloudFront distribution |
| Wake/idle control | Lambda function, Function URL, EventBridge schedule, DynamoDB state |
| Runtime | Existing EC2 instance, Docker Compose, nginx |
| Durable data | PostgreSQL containers on encrypted EBS |
| Configuration | SSM SecureString and instance role |
| Backups/state | Private encrypted S3 bucket |
| Images | GitHub Container Registry |
| Frontend | Vercel |

CloudFront sends a private origin header that the controller and nginx validate. The browser never receives the header, instance role, SSM value, or database credentials. Security groups expose only the required public origin path; PostgreSQL ports stay inside Docker networking.

The controller starts exactly the configured instance, waits for dependency readiness, records activity, permits up to 24 hours of runtime per UTC day, and stops the host after 20 idle minutes. Terraform manages the controller and guardrails; host application updates are delivered through SSM so routine releases do not replace the instance or its data volume.

This is intentionally a single-host showcase topology. A production design would require multi-AZ data services, replicated messaging, distributed SignalR backplanes, private origins, formal recovery objectives, secrets rotation, capacity planning, and an application-specific threat model.
