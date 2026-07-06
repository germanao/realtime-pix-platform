#!/usr/bin/env bash
set -euo pipefail

# Deletes the manual Azure POC after taking a read-only inventory.
# This intentionally discards fictional data, Key Vault secrets, ACR images,
# logs, and any resources in the selected resource group.

RG="${RG:-rg-realtime-pix-poc}"
TFSTATE_RG="${TFSTATE_RG:-rg-realtime-pix-tfstate}"
CONFIRM_DELETE="${CONFIRM_DELETE:-}"
DELETE_TFSTATE_RG="${DELETE_TFSTATE_RG:-no}"

if [[ "${CONFIRM_DELETE}" != "delete-${RG}" ]]; then
  echo "Refusing to delete resources."
  echo "Run with: CONFIRM_DELETE=delete-${RG} $0"
  exit 2
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
"${SCRIPT_DIR}/azure-inventory.sh"

if az group show --name "${RG}" --output none 2>/dev/null; then
  echo "Deleting resource group ${RG}..."
  az group delete --name "${RG}" --yes --no-wait
else
  echo "Resource group ${RG} was already absent."
fi

if [[ "${DELETE_TFSTATE_RG}" == "yes" ]]; then
  if az group show --name "${TFSTATE_RG}" --output none 2>/dev/null; then
    echo "Deleting Terraform state resource group ${TFSTATE_RG}..."
    az group delete --name "${TFSTATE_RG}" --yes --no-wait
  fi
fi

echo "Delete requested. Wait a few minutes, then run:"
echo "  az group exists --name ${RG}"
echo "If any deleted Key Vault cannot be purged because purge protection is enabled, record its retention date and use a new suffix."
