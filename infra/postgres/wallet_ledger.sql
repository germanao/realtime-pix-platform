create table if not exists accounts (
    account_id text primary key,
    user_id text not null,
    bank_name text not null,
    balance numeric(18, 2) not null default 0,
    version integer not null default 0
);

create table if not exists ledger_entries (
    ledger_entry_id text primary key,
    account_id text not null references accounts(account_id),
    user_id text not null,
    amount numeric(18, 2) not null,
    balance_after numeric(18, 2) not null,
    direction text not null,
    description text not null,
    occurred_at timestamptz not null
);

create index if not exists ix_ledger_entries_account_occurred
    on ledger_entries(account_id, occurred_at desc);

create table if not exists outbox_messages (
    id uuid primary key,
    event_type text not null,
    payload_json jsonb not null,
    occurred_at timestamptz not null,
    published_at timestamptz,
    publish_attempts integer not null default 0
);

create table if not exists inbox_messages (
    event_id uuid not null,
    consumer_name text not null,
    event_type text not null,
    received_at timestamptz not null,
    processed_at timestamptz,
    error text,
    primary key (event_id, consumer_name)
);

