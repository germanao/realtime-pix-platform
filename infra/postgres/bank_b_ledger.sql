\set ON_ERROR_STOP on

-- Run as the PostgreSQL administrator after creating bank_b_app. EF migrations
-- remain the schema authority; this script only establishes database ownership.
revoke connect on database bank_b_ledger_db from public;
grant connect on database bank_b_ledger_db to bank_b_app, azure_pg_admin;
revoke create on schema public from public;
grant usage, create on schema public to bank_b_app;
alter default privileges in schema public grant select, insert, update, delete on tables to bank_b_app;
alter default privileges in schema public grant usage, select, update on sequences to bank_b_app;
