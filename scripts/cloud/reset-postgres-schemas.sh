#!/usr/bin/env bash
set -euo pipefail

: "${PGHOST:?Set PGHOST to the PostgreSQL Flexible Server FQDN.}"
: "${KEYVAULT_NAME:?Set KEYVAULT_NAME.}"

connection_password() {
  local secret_name="$1"
  local connection_string

  connection_string="$(az keyvault secret show --vault-name "${KEYVAULT_NAME}" --name "${secret_name}" --query value -o tsv)"
  printf "%s" "${connection_string}" | tr ';' '\n' | sed -n 's/^Password=//p' | head -n 1
}

reset_schema() {
  local secret_name="$1"
  local db_user="$2"
  local db_name="$3"
  local password

  password="$(connection_password "${secret_name}")"
  if [[ -z "${password}" ]]; then
    echo "Could not read password from Key Vault secret ${secret_name}." >&2
    exit 1
  fi

  echo "Resetting schema public in ${db_name}."
  PGPASSWORD="${password}" psql "host=${PGHOST} port=5432 dbname=${db_name} user=${db_user} sslmode=require" -v ON_ERROR_STOP=1 <<SQL
DROP SCHEMA IF EXISTS public CASCADE;
CREATE SCHEMA public AUTHORIZATION ${db_user};
GRANT USAGE, CREATE ON SCHEMA public TO ${db_user};
SQL

  unset password
}

reset_schema "identity-db" "identity_app" "identity_presence_db"
reset_schema "wallet-db" "wallet_app" "wallet_ledger_db"
reset_schema "transaction-db" "transaction_app" "transaction_db"
reset_schema "realtime-db" "realtime_app" "realtime_projection_db"

echo "PostgreSQL public schemas were reset. This intentionally deleted disposable POC data."
