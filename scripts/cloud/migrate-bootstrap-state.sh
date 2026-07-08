#!/usr/bin/env bash
set -euo pipefail

# Migrates the bootstrap stack local state into the Azure Blob backend that the
# bootstrap stack created. Run this from Azure Cloud Shell after bootstrap apply.

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
BOOTSTRAP_DIR="${REPO_ROOT}/infra/terraform/bootstrap"

cd "${BOOTSTRAP_DIR}"

if [[ ! -f terraform.tfstate ]]; then
  echo "terraform.tfstate was not found in ${BOOTSTRAP_DIR}."
  echo "Run this script from the same Cloud Shell clone where bootstrap apply succeeded."
  exit 2
fi

TFSTATE_RESOURCE_GROUP="${TFSTATE_RESOURCE_GROUP:-$(terraform output -raw tfstate_resource_group_name)}"
TFSTATE_STORAGE_ACCOUNT="${TFSTATE_STORAGE_ACCOUNT:-$(terraform output -raw tfstate_storage_account_name)}"
TFSTATE_CONTAINER="${TFSTATE_CONTAINER:-$(terraform output -raw tfstate_container_name)}"
TFSTATE_KEY="${TFSTATE_KEY:-bootstrap.tfstate}"

echo "Migrating bootstrap state to:"
echo "  resource group : ${TFSTATE_RESOURCE_GROUP}"
echo "  storage account: ${TFSTATE_STORAGE_ACCOUNT}"
echo "  container      : ${TFSTATE_CONTAINER}"
echo "  key            : ${TFSTATE_KEY}"

set +e
terraform init -migrate-state -force-copy \
  -backend-config="resource_group_name=${TFSTATE_RESOURCE_GROUP}" \
  -backend-config="storage_account_name=${TFSTATE_STORAGE_ACCOUNT}" \
  -backend-config="container_name=${TFSTATE_CONTAINER}" \
  -backend-config="key=${TFSTATE_KEY}" \
  -backend-config="use_azuread_auth=true"
result=$?
set -e

if [[ ${result} -eq 0 ]]; then
  echo "Bootstrap state migration succeeded with Azure AD auth."
  exit 0
fi

cat <<EOF

Azure AD backend migration failed, most commonly because your signed-in Cloud
Shell user does not have Storage Blob Data Contributor on the Terraform state
storage account.

You can either grant yourself that role and rerun this script, or use the
temporary storage-key fallback below.

Grant yourself data-plane access:

  SCOPE="\$(az storage account show --resource-group "${TFSTATE_RESOURCE_GROUP}" --name "${TFSTATE_STORAGE_ACCOUNT}" --query id -o tsv)"
  USER_OBJECT_ID="\$(az ad signed-in-user show --query id -o tsv)"
  az role assignment create --assignee-object-id "\${USER_OBJECT_ID}" --assignee-principal-type User --role "Storage Blob Data Contributor" --scope "\${SCOPE}"
  sleep 90
  scripts/cloud/migrate-bootstrap-state.sh

Temporary storage-key fallback:

  ALLOW_STORAGE_KEY_FALLBACK=yes scripts/cloud/migrate-bootstrap-state.sh

EOF

if [[ "${ALLOW_STORAGE_KEY_FALLBACK:-no}" != "yes" ]]; then
  exit "${result}"
fi

echo "Trying temporary storage-key fallback..."
export ARM_ACCESS_KEY
ARM_ACCESS_KEY="$(az storage account keys list \
  --resource-group "${TFSTATE_RESOURCE_GROUP}" \
  --account-name "${TFSTATE_STORAGE_ACCOUNT}" \
  --query "[0].value" \
  -o tsv)"

terraform init -migrate-state -force-copy \
  -backend-config="resource_group_name=${TFSTATE_RESOURCE_GROUP}" \
  -backend-config="storage_account_name=${TFSTATE_STORAGE_ACCOUNT}" \
  -backend-config="container_name=${TFSTATE_CONTAINER}" \
  -backend-config="key=${TFSTATE_KEY}"

unset ARM_ACCESS_KEY

echo "Bootstrap state migration succeeded with the temporary storage-key fallback."
