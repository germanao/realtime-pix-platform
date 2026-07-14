#!/usr/bin/env bash
set -euo pipefail

: "${PGHOST:?Set PGHOST to the PostgreSQL Flexible Server FQDN.}"
: "${PGADMIN_USER:?Set PGADMIN_USER to the Microsoft Entra administrator name.}"
: "${WORKLOAD_IDENTITIES_JSON:?Set WORKLOAD_IDENTITIES_JSON from the runtime Terraform output.}"

ENTRA_TOKEN="$(az account get-access-token --resource-type oss-rdbms --query accessToken -o tsv)"

run_migration() {
  local database_name="$1"
  local project_path="$2"
  local service_name="$3"
  local migrations_connection_variable="$4"
  local identity_key="$5"
  local bundle_path="work/migration-bundles/${service_name}"
  local connection_string="Host=${PGHOST};Port=5432;Database=${database_name};Username=${PGADMIN_USER};Password=${ENTRA_TOKEN};SSL Mode=Require;Trust Server Certificate=false"
  local service_role
  service_role="$(printf '%s' "${WORKLOAD_IDENTITIES_JSON}" | jq -er --arg key "${identity_key}" '.[$key].name')"

  export "${migrations_connection_variable}=${connection_string}"
  echo "Building EF migration bundle for ${service_name}."
  dotnet tool run dotnet-ef migrations bundle \
    --project "${project_path}" \
    --configuration Release \
    --no-build \
    --force \
    --output "${bundle_path}"

  echo "Applying EF migration bundle for ${service_name} to ${database_name}."
  "${bundle_path}" --connection "${connection_string}"

  PGPASSWORD="${ENTRA_TOKEN}" psql \
    "host=${PGHOST} port=5432 dbname=${database_name} user=${PGADMIN_USER} sslmode=require" \
    -v ON_ERROR_STOP=1 \
    -v admin_name="${PGADMIN_USER}" \
    -v service_role="${service_role}" <<'SQL'
REASSIGN OWNED BY :"admin_name" TO :"service_role";
SQL
  unset "${migrations_connection_variable}"
}

dotnet tool restore
dotnet restore RealtimePixPlatform.slnx
dotnet build RealtimePixPlatform.slnx --configuration Release --no-restore --maxcpucount:1
mkdir -p work/migration-bundles

run_migration identity_presence_db "services/identity-presence-service/IdentityPresence.Infrastructure/IdentityPresence.Infrastructure.csproj" identity-presence-service IDENTITY_PRESENCE_MIGRATIONS_CONNECTION identity_presence
run_migration bank_a_ledger_db "services/bank-ledger-service/BankLedger.Infrastructure/BankLedger.Infrastructure.csproj" bank-a-ledger-service BANK_LEDGER_MIGRATIONS_CONNECTION bank_a
run_migration bank_b_ledger_db "services/bank-ledger-service/BankLedger.Infrastructure/BankLedger.Infrastructure.csproj" bank-b-ledger-service BANK_LEDGER_MIGRATIONS_CONNECTION bank_b
run_migration transaction_db "services/transaction-service/Transaction.Infrastructure/Transaction.Infrastructure.csproj" transaction-service TRANSACTION_MIGRATIONS_CONNECTION transaction
run_migration realtime_projection_db "services/realtime-events-service/RealtimeEvents.Infrastructure/RealtimeEvents.Infrastructure.csproj" realtime-events-service REALTIME_PROJECTION_MIGRATIONS_CONNECTION realtime_events

unset ENTRA_TOKEN
echo "EF migrations applied to all five service-owned databases."
