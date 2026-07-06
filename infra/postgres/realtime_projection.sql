CREATE TABLE IF NOT EXISTS timeline_events (
    event_id text PRIMARY KEY,
    event_type text NOT NULL,
    producer text NOT NULL,
    transfer_id text NULL,
    correlation_id text NOT NULL,
    occurred_at timestamptz NOT NULL,
    payload jsonb NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_timeline_events_occurred_at
    ON timeline_events (occurred_at DESC);

CREATE TABLE IF NOT EXISTS transfer_flow_steps (
    step_id text PRIMARY KEY,
    source_event_id text NOT NULL UNIQUE,
    transfer_id text NOT NULL,
    event_type text NOT NULL,
    stage text NOT NULL,
    title text NOT NULL,
    detail text NOT NULL,
    recorded_at timestamptz NOT NULL,
    producer text NOT NULL,
    correlation_id text NOT NULL,
    causation_id text NULL,
    outcome text NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_transfer_flow_steps_transfer_id_recorded_at
    ON transfer_flow_steps (transfer_id, recorded_at);
