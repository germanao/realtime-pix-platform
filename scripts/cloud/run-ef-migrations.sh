#!/usr/bin/env bash
set -euo pipefail

: "${KEYVAULT_NAME:?Set KEYVAULT_NAME.}"

run_migration() {
  local secret_name="$1"
  local project_path="$2"
  local service_name="$3"
  local migrations_connection_variable="$4"
  local connection_string
  local bundle_path="work/migration-bundles/${service_name}"

  connection_string="$(az keyvault secret show --vault-name "${KEYVAULT_NAME}" --name "${secret_name}" --query value -o tsv)"
  export "${migrations_connection_variable}=${connection_string}"

  echo "Building EF migration bundle for ${service_name}."
  dotnet tool run dotnet-ef migrations bundle \
    --project "${project_path}" \
    --configuration Release \
    --no-build \
    --force \
    --output "${bundle_path}"

  echo "Applying EF migration bundle for ${service_name}."
  "${bundle_path}" --connection "${connection_string}"

  unset "${migrations_connection_variable}"
  unset connection_string
}

dotnet tool restore
dotnet restore RealtimePixPlatform.slnx
dotnet build RealtimePixPlatform.slnx --configuration Release --no-restore
mkdir -p work/migration-bundles

run_migration "identity-db" "services/identity-presence-service/IdentityPresenceService.csproj" "identity-presence-service" "IDENTITY_PRESENCE_MIGRATIONS_CONNECTION"
run_migration "wallet-db" "services/wallet-ledger-service/WalletLedgerService.csproj" "wallet-ledger-service" "WALLET_LEDGER_MIGRATIONS_CONNECTION"
run_migration "transaction-db" "services/transaction-service/TransactionService.csproj" "transaction-service" "TRANSACTION_MIGRATIONS_CONNECTION"
run_migration "realtime-db" "services/realtime-events-service/RealtimeEventsService.csproj" "realtime-events-service" "REALTIME_PROJECTION_MIGRATIONS_CONNECTION"

echo "EF migrations applied."
