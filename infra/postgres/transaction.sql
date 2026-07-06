create table if not exists pix_transfers (
    transfer_id text primary key,
    idempotency_key text not null unique,
    sender_user_id text not null,
    sender_account_id text not null,
    recipient_user_id text not null,
    recipient_account_id text not null,
    amount numeric(18, 2) not null,
    status text not null,
    failure_reason text,
    created_at timestamptz not null,
    updated_at timestamptz not null
);

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

