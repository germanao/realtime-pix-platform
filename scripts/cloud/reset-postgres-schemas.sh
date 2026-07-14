#!/usr/bin/env bash
set -euo pipefail

: "${PGHOST:?Set PGHOST to the PostgreSQL Flexible Server FQDN.}"
: "${PGADMIN_USER:?Set PGADMIN_USER to the Microsoft Entra administrator name.}"
: "${WORKLOAD_IDENTITIES_JSON:?Set WORKLOAD_IDENTITIES_JSON from the runtime Terraform output.}"

ENTRA_TOKEN="$(az account get-access-token --resource-type oss-rdbms --query accessToken -o tsv)"

reset_database() {
  local identity_key="$1"
  local database_name="$2"
  local service_role
  service_role="$(printf '%s' "${WORKLOAD_IDENTITIES_JSON}" | jq -er --arg key "${identity_key}" '.[$key].name')"

  echo "Resetting disposable POC data in ${database_name}."
  PGPASSWORD="${ENTRA_TOKEN}" psql \
    "host=${PGHOST} port=5432 dbname=${database_name} user=${PGADMIN_USER} sslmode=require" \
    -v ON_ERROR_STOP=1 \
    -v service_role="${service_role}" <<'SQL'
SET ROLE :"service_role";
DROP SCHEMA public CASCADE;
CREATE SCHEMA public AUTHORIZATION :"service_role";
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
RESET ROLE;
SQL
}

reset_database identity_presence identity_presence_db
reset_database bank_a bank_a_ledger_db
reset_database bank_b bank_b_ledger_db
reset_database transaction transaction_db
reset_database realtime_events realtime_projection_db

unset ENTRA_TOKEN
echo "All five service-owned POC schemas were reset."
