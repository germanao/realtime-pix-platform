create table if not exists anonymous_sessions (
    user_id text primary key,
    client_id text not null unique,
    display_name text not null,
    last_seen_at timestamptz not null
);

create table if not exists presence_users (
    user_id text primary key,
    display_name text not null,
    is_bot boolean not null,
    last_seen_at timestamptz not null
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

