#!/usr/bin/env bash
set -euo pipefail

OWNER="${OWNER:-germanao}"
REPO="${REPO:-realtime-pix-platform}"
ENVIRONMENT="${ENVIRONMENT:-poc}"

: "${AZURE_CLIENT_ID:?Set AZURE_CLIENT_ID from bootstrap Terraform output.}"
: "${AZURE_TENANT_ID:?Set AZURE_TENANT_ID from bootstrap Terraform output.}"
: "${AZURE_SUBSCRIPTION_ID:?Set AZURE_SUBSCRIPTION_ID from bootstrap Terraform output.}"
: "${TFSTATE_RESOURCE_GROUP:?Set TFSTATE_RESOURCE_GROUP from bootstrap Terraform output.}"
: "${TFSTATE_STORAGE_ACCOUNT:?Set TFSTATE_STORAGE_ACCOUNT from bootstrap Terraform output.}"
: "${TFSTATE_CONTAINER:?Set TFSTATE_CONTAINER from bootstrap Terraform output.}"

gh variable set AZURE_CLIENT_ID --repo "${OWNER}/${REPO}" --env "${ENVIRONMENT}" --body "${AZURE_CLIENT_ID}"
gh variable set AZURE_TENANT_ID --repo "${OWNER}/${REPO}" --env "${ENVIRONMENT}" --body "${AZURE_TENANT_ID}"
gh variable set AZURE_SUBSCRIPTION_ID --repo "${OWNER}/${REPO}" --env "${ENVIRONMENT}" --body "${AZURE_SUBSCRIPTION_ID}"
gh variable set TFSTATE_RESOURCE_GROUP --repo "${OWNER}/${REPO}" --env "${ENVIRONMENT}" --body "${TFSTATE_RESOURCE_GROUP}"
gh variable set TFSTATE_STORAGE_ACCOUNT --repo "${OWNER}/${REPO}" --env "${ENVIRONMENT}" --body "${TFSTATE_STORAGE_ACCOUNT}"
gh variable set TFSTATE_CONTAINER --repo "${OWNER}/${REPO}" --env "${ENVIRONMENT}" --body "${TFSTATE_CONTAINER}"

echo "GitHub environment variables were set for ${OWNER}/${REPO}:${ENVIRONMENT}."
echo "Set secrets separately:"
echo "  gh secret set VERCEL_API_TOKEN --repo ${OWNER}/${REPO} --env ${ENVIRONMENT}"
