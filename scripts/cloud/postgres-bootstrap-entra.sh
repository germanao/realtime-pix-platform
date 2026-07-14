#!/usr/bin/env bash
set -euo pipefail

: "${PGHOST:?Set PGHOST to the PostgreSQL Flexible Server FQDN.}"
: "${PGADMIN_USER:?Set PGADMIN_USER to the Microsoft Entra administrator name.}"
: "${WORKLOAD_IDENTITIES_JSON:?Set WORKLOAD_IDENTITIES_JSON from the runtime Terraform output.}"

ENTRA_TOKEN="$(az account get-access-token --resource-type oss-rdbms --query accessToken -o tsv)"

principal_value() {
  local key="$1"
  local field="$2"
  printf '%s' "${WORKLOAD_IDENTITIES_JSON}" | jq -er --arg key "${key}" --arg field "${field}" '.[$key][$field]'
}

create_principal() {
  local key="$1"
  local role_name
  local object_id
  role_name="$(principal_value "${key}" name)"
  object_id="$(principal_value "${key}" principal_id)"

  PGPASSWORD="${ENTRA_TOKEN}" psql \
    "host=${PGHOST} port=5432 dbname=postgres user=${PGADMIN_USER} sslmode=require" \
    -v ON_ERROR_STOP=1 \
    -v role_name="${role_name}" \
    -v object_id="${object_id}" \
    -v admin_name="${PGADMIN_USER}" <<'SQL'
SELECT pg_catalog.pgaadauth_create_principal_with_oid(
  :'role_name',
  :'object_id',
  'service',
  false,
  false
)
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = :'role_name');
GRANT :"role_name" TO :"admin_name";
SQL
}

grant_database() {
  local key="$1"
  local database_name="$2"
  local role_name
  role_name="$(principal_value "${key}" name)"

  PGPASSWORD="${ENTRA_TOKEN}" psql \
    "host=${PGHOST} port=5432 dbname=postgres user=${PGADMIN_USER} sslmode=require" \
    -v ON_ERROR_STOP=1 \
    -v role_name="${role_name}" \
    -v database_name="${database_name}" <<'SQL'
REVOKE CONNECT ON DATABASE :"database_name" FROM PUBLIC;
GRANT CONNECT ON DATABASE :"database_name" TO :"role_name", azure_pg_admin;
ALTER DATABASE :"database_name" OWNER TO :"role_name";
SQL

  PGPASSWORD="${ENTRA_TOKEN}" psql \
    "host=${PGHOST} port=5432 dbname=${database_name} user=${PGADMIN_USER} sslmode=require" \
    -v ON_ERROR_STOP=1 \
    -v role_name="${role_name}" <<'SQL'
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT USAGE, CREATE ON SCHEMA public TO :"role_name";
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO :"role_name";
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT USAGE, SELECT, UPDATE ON SEQUENCES TO :"role_name";
SQL
}

for key in identity_presence bank_a bank_b transaction realtime_events; do
  create_principal "${key}"
done

grant_database identity_presence identity_presence_db
grant_database bank_a bank_a_ledger_db
grant_database bank_b bank_b_ledger_db
grant_database transaction transaction_db
grant_database realtime_events realtime_projection_db

unset ENTRA_TOKEN
echo "Microsoft Entra database principals and least-privilege ownership are configured."
