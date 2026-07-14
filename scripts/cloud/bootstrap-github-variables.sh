#!/usr/bin/env bash
set -euo pipefail

OWNER="${OWNER:-germanao}"
REPO="${REPO:-realtime-pix-platform}"
ENVIRONMENT="${ENVIRONMENT:-poc}"

: "${AZURE_PLAN_CLIENT_ID:?Set AZURE_PLAN_CLIENT_ID from the github_plan_client_id output.}"
: "${AZURE_IMAGE_CLIENT_ID:?Set AZURE_IMAGE_CLIENT_ID from the github_image_push_client_id output.}"
: "${AZURE_TENANT_ID:?Set AZURE_TENANT_ID from bootstrap Terraform output.}"
: "${AZURE_SUBSCRIPTION_ID:?Set AZURE_SUBSCRIPTION_ID from bootstrap Terraform output.}"
: "${TFSTATE_RESOURCE_GROUP:?Set TFSTATE_RESOURCE_GROUP from bootstrap Terraform output.}"
: "${TFSTATE_STORAGE_ACCOUNT:?Set TFSTATE_STORAGE_ACCOUNT from bootstrap Terraform output.}"
: "${TFSTATE_CONTAINER:?Set TFSTATE_CONTAINER from bootstrap Terraform output.}"

AZURE_APPLY_CLIENT_ID="${AZURE_APPLY_CLIENT_ID:-${AZURE_CLIENT_ID:-}}"
: "${AZURE_APPLY_CLIENT_ID:?Set AZURE_APPLY_CLIENT_ID from the github_apply_client_id output.}"

gh variable set AZURE_APPLY_CLIENT_ID --repo "${OWNER}/${REPO}" --env "${ENVIRONMENT}" --body "${AZURE_APPLY_CLIENT_ID}"
gh variable set AZURE_IMAGE_CLIENT_ID --repo "${OWNER}/${REPO}" --env "${ENVIRONMENT}" --body "${AZURE_IMAGE_CLIENT_ID}"
gh variable set AZURE_CLIENT_ID --repo "${OWNER}/${REPO}" --env "${ENVIRONMENT}" --body "${AZURE_APPLY_CLIENT_ID}"
gh variable set AZURE_TENANT_ID --repo "${OWNER}/${REPO}" --env "${ENVIRONMENT}" --body "${AZURE_TENANT_ID}"
gh variable set AZURE_SUBSCRIPTION_ID --repo "${OWNER}/${REPO}" --env "${ENVIRONMENT}" --body "${AZURE_SUBSCRIPTION_ID}"
gh variable set TFSTATE_RESOURCE_GROUP --repo "${OWNER}/${REPO}" --env "${ENVIRONMENT}" --body "${TFSTATE_RESOURCE_GROUP}"
gh variable set TFSTATE_STORAGE_ACCOUNT --repo "${OWNER}/${REPO}" --env "${ENVIRONMENT}" --body "${TFSTATE_STORAGE_ACCOUNT}"
gh variable set TFSTATE_CONTAINER --repo "${OWNER}/${REPO}" --env "${ENVIRONMENT}" --body "${TFSTATE_CONTAINER}"

# Pull-request plans do not use an environment, so their non-secret identifiers
# must also exist at repository scope.
gh variable set AZURE_PLAN_CLIENT_ID --repo "${OWNER}/${REPO}" --body "${AZURE_PLAN_CLIENT_ID}"
gh variable set AZURE_TENANT_ID --repo "${OWNER}/${REPO}" --body "${AZURE_TENANT_ID}"
gh variable set AZURE_SUBSCRIPTION_ID --repo "${OWNER}/${REPO}" --body "${AZURE_SUBSCRIPTION_ID}"
gh variable set TFSTATE_RESOURCE_GROUP --repo "${OWNER}/${REPO}" --body "${TFSTATE_RESOURCE_GROUP}"
gh variable set TFSTATE_STORAGE_ACCOUNT --repo "${OWNER}/${REPO}" --body "${TFSTATE_STORAGE_ACCOUNT}"
gh variable set TFSTATE_CONTAINER --repo "${OWNER}/${REPO}" --body "${TFSTATE_CONTAINER}"

if [[ -n "${PUBLISHER_EMAIL:-}" ]]; then
  gh variable set PUBLISHER_EMAIL --repo "${OWNER}/${REPO}" --env "${ENVIRONMENT}" --body "${PUBLISHER_EMAIL}"
  gh variable set PUBLISHER_EMAIL --repo "${OWNER}/${REPO}" --body "${PUBLISHER_EMAIL}"
fi

echo "GitHub repository and ${ENVIRONMENT} environment variables were configured for ${OWNER}/${REPO}."
echo "Configure Vercel separately after runtime deployment using Terraform outputs:"
echo "  NEXT_PUBLIC_API_BASE_URL"
echo "  NEXT_PUBLIC_PRESENCE_HUB_URL"
echo "  NEXT_PUBLIC_EVENTS_HUB_URL"
