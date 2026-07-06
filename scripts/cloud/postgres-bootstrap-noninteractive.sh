#!/usr/bin/env bash
set -euo pipefail

# GitHub Actions-safe database bootstrap.
# Requires Key Vault secret postgres-admin-password created by Terraform.

: "${PGHOST:?Set PGHOST to the PostgreSQL Flexible Server FQDN.}"
: "${PGADMIN_USER:=pixadmin}"
: "${KEYVAULT_NAME:?Set KEYVAULT_NAME.}"

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

PGADMIN_PASSWORD="$(az keyvault secret show --vault-name "${KEYVAULT_NAME}" --name "postgres-admin-password" --query value -o tsv)"
IDENTITY_PASSWORD="$(openssl rand -base64 32 | tr -d '\n')"
WALLET_PASSWORD="$(openssl rand -base64 32 | tr -d '\n')"
TRANSACTION_PASSWORD="$(openssl rand -base64 32 | tr -d '\n')"
REALTIME_PASSWORD="$(openssl rand -base64 32 | tr -d '\n')"

escape_sql_literal() {
  printf "%s" "$1" | sed "s/'/''/g"
}

tmp_sql="$(mktemp)"
cat > "${tmp_sql}" <<SQL
DO \$\$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'identity_app') THEN
    CREATE ROLE identity_app LOGIN PASSWORD '$(escape_sql_literal "${IDENTITY_PASSWORD}")';
  ELSE
    ALTER ROLE identity_app WITH LOGIN PASSWORD '$(escape_sql_literal "${IDENTITY_PASSWORD}")';
  END IF;

  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'wallet_app') THEN
    CREATE ROLE wallet_app LOGIN PASSWORD '$(escape_sql_literal "${WALLET_PASSWORD}")';
  ELSE
    ALTER ROLE wallet_app WITH LOGIN PASSWORD '$(escape_sql_literal "${WALLET_PASSWORD}")';
  END IF;

  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'transaction_app') THEN
    CREATE ROLE transaction_app LOGIN PASSWORD '$(escape_sql_literal "${TRANSACTION_PASSWORD}")';
  ELSE
    ALTER ROLE transaction_app WITH LOGIN PASSWORD '$(escape_sql_literal "${TRANSACTION_PASSWORD}")';
  END IF;

  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'realtime_app') THEN
    CREATE ROLE realtime_app LOGIN PASSWORD '$(escape_sql_literal "${REALTIME_PASSWORD}")';
  ELSE
    ALTER ROLE realtime_app WITH LOGIN PASSWORD '$(escape_sql_literal "${REALTIME_PASSWORD}")';
  END IF;
END
\$\$;

ALTER DATABASE identity_presence_db OWNER TO identity_app;
ALTER DATABASE wallet_ledger_db OWNER TO wallet_app;
ALTER DATABASE transaction_db OWNER TO transaction_app;
ALTER DATABASE realtime_projection_db OWNER TO realtime_app;

REVOKE CONNECT ON DATABASE identity_presence_db FROM PUBLIC;
REVOKE CONNECT ON DATABASE wallet_ledger_db FROM PUBLIC;
REVOKE CONNECT ON DATABASE transaction_db FROM PUBLIC;
REVOKE CONNECT ON DATABASE realtime_projection_db FROM PUBLIC;

GRANT CONNECT ON DATABASE identity_presence_db TO identity_app, azure_pg_admin;
GRANT CONNECT ON DATABASE wallet_ledger_db TO wallet_app, azure_pg_admin;
GRANT CONNECT ON DATABASE transaction_db TO transaction_app, azure_pg_admin;
GRANT CONNECT ON DATABASE realtime_projection_db TO realtime_app, azure_pg_admin;
SQL

PGPASSWORD="${PGADMIN_PASSWORD}" psql "host=${PGHOST} port=5432 dbname=postgres user=${PGADMIN_USER} sslmode=require" -v ON_ERROR_STOP=1 -f "${tmp_sql}"
rm -f "${tmp_sql}"

grant_and_schema() {
  local db_user="$1"
  local db_name="$2"
  local password="$3"
  local schema_file="$4"

  PGPASSWORD="${PGADMIN_PASSWORD}" psql "host=${PGHOST} port=5432 dbname=${db_name} user=${PGADMIN_USER} sslmode=require" -v ON_ERROR_STOP=1 <<SQL
GRANT USAGE, CREATE ON SCHEMA public TO ${db_user};
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO ${db_user};
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT USAGE, SELECT, UPDATE ON SEQUENCES TO ${db_user};
SQL

  PGPASSWORD="${password}" psql "host=${PGHOST} port=5432 dbname=${db_name} user=${db_user} sslmode=require" -v ON_ERROR_STOP=1 -f "${repo_root}/${schema_file}"
}

grant_and_schema identity_app identity_presence_db "${IDENTITY_PASSWORD}" "infra/postgres/identity_presence.sql"
grant_and_schema wallet_app wallet_ledger_db "${WALLET_PASSWORD}" "infra/postgres/wallet_ledger.sql"
grant_and_schema transaction_app transaction_db "${TRANSACTION_PASSWORD}" "infra/postgres/transaction.sql"
grant_and_schema realtime_app realtime_projection_db "${REALTIME_PASSWORD}" "infra/postgres/realtime_projection.sql"

az keyvault secret set --vault-name "${KEYVAULT_NAME}" --name "identity-db" --value "Host=${PGHOST};Port=5432;Database=identity_presence_db;Username=identity_app;Password=${IDENTITY_PASSWORD};SSL Mode=Require;Trust Server Certificate=false;Maximum Pool Size=10;Minimum Pool Size=0" --output none
az keyvault secret set --vault-name "${KEYVAULT_NAME}" --name "wallet-db" --value "Host=${PGHOST};Port=5432;Database=wallet_ledger_db;Username=wallet_app;Password=${WALLET_PASSWORD};SSL Mode=Require;Trust Server Certificate=false;Maximum Pool Size=10;Minimum Pool Size=0" --output none
az keyvault secret set --vault-name "${KEYVAULT_NAME}" --name "transaction-db" --value "Host=${PGHOST};Port=5432;Database=transaction_db;Username=transaction_app;Password=${TRANSACTION_PASSWORD};SSL Mode=Require;Trust Server Certificate=false;Maximum Pool Size=10;Minimum Pool Size=0" --output none
az keyvault secret set --vault-name "${KEYVAULT_NAME}" --name "realtime-db" --value "Host=${PGHOST};Port=5432;Database=realtime_projection_db;Username=realtime_app;Password=${REALTIME_PASSWORD};SSL Mode=Require;Trust Server Certificate=false;Maximum Pool Size=10;Minimum Pool Size=0" --output none

unset PGADMIN_PASSWORD IDENTITY_PASSWORD WALLET_PASSWORD TRANSACTION_PASSWORD REALTIME_PASSWORD
echo "PostgreSQL roles, schemas, and Key Vault secrets are configured."
