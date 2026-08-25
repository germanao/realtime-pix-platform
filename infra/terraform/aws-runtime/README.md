# AWS scheduled runtime

This stack keeps the request-serving compute tier warm on a single free-tier-eligible `c7i-flex.large` EC2 host from 09:00–15:00 on weekdays in `America/Sao_Paulo`. The existing Azure Container Apps remain at scale-to-zero as the fallback deployment.

The AWS free-plan project currently enforces `us-east-2` with an organization service-control policy. Changing the AWS CLI default region does not change that policy.

Before applying, create the encrypted `/realtime-pix/poc/compose-env` SSM parameter. Never commit its value. The deployment workflow publishes x86-64 images to the existing public GHCR packages under the `aws-latest` tag.
