#!/usr/bin/env bash
set -euo pipefail

: "${TFSTATE_STORAGE_ACCOUNT:?Set TFSTATE_STORAGE_ACCOUNT.}"
: "${TFSTATE_CONTAINER:?Set TFSTATE_CONTAINER.}"

ENVIRONMENT_NAME="${ENVIRONMENT_NAME:-poc}"
WORK_DIR="$(mktemp -d)"
trap 'rm -rf "${WORK_DIR}"' EXIT

blob_exists() {
  az storage blob exists \
    --account-name "${TFSTATE_STORAGE_ACCOUNT}" \
    --container-name "${TFSTATE_CONTAINER}" \
    --name "$1" \
    --auth-mode login \
    --query exists \
    --output tsv
}

download_blob() {
  az storage blob download \
    --account-name "${TFSTATE_STORAGE_ACCOUNT}" \
    --container-name "${TFSTATE_CONTAINER}" \
    --name "$1" \
    --file "$2" \
    --auth-mode login \
    --overwrite \
    --only-show-errors \
    --output none
}

migrate_state() {
  local source_key="$1"
  local destination_key="$2"
  local source_file="${WORK_DIR}/source.tfstate"
  local destination_file="${WORK_DIR}/destination.tfstate"
  local source_exists destination_exists

  source_exists="$(blob_exists "${source_key}")"
  destination_exists="$(blob_exists "${destination_key}")"

  if [[ "${source_exists}" != "true" ]]; then
    if [[ "${destination_exists}" == "true" ]]; then
      echo "${destination_key} already exists; no migration is required."
      return
    fi

    echo "Neither ${source_key} nor ${destination_key} exists." >&2
    exit 1
  fi

  download_blob "${source_key}" "${source_file}"

  if [[ "${destination_exists}" == "true" ]]; then
    download_blob "${destination_key}" "${destination_file}"
    if ! cmp --silent "${source_file}" "${destination_file}"; then
      echo "Both state keys exist with different contents: ${source_key}, ${destination_key}." >&2
      echo "Resolve the divergence manually before running Terraform." >&2
      exit 1
    fi

    echo "${destination_key} already matches ${source_key}."
    return
  fi

  az storage blob upload \
    --account-name "${TFSTATE_STORAGE_ACCOUNT}" \
    --container-name "${TFSTATE_CONTAINER}" \
    --name "${destination_key}" \
    --file "${source_file}" \
    --auth-mode login \
    --overwrite false \
    --metadata "migratedFrom=${source_key}" \
    --only-show-errors \
    --output none

  download_blob "${destination_key}" "${destination_file}"
  cmp --silent "${source_file}" "${destination_file}" || {
    echo "Verification failed for ${destination_key}." >&2
    exit 1
  }

  echo "Verified ${source_key} -> ${destination_key}. The source remains as a recovery copy."
}

migrate_state "foundation-poc.tfstate" "${ENVIRONMENT_NAME}/foundation.tfstate"
migrate_state "runtime-poc.tfstate" "${ENVIRONMENT_NAME}/runtime.tfstate"

echo "Environment-scoped state migration completed successfully."
