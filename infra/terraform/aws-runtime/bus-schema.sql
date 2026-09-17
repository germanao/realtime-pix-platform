CREATE TABLE IF NOT EXISTS bus_messages (
    id uuid PRIMARY KEY,
    sequence bigint GENERATED ALWAYS AS IDENTITY UNIQUE,
    kind text NOT NULL,
    destination text NOT NULL,
    envelope jsonb NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE IF NOT EXISTS bus_acknowledgements (
    consumer text NOT NULL,
    message_id uuid NOT NULL REFERENCES bus_messages(id) ON DELETE CASCADE,
    acknowledged_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (consumer, message_id)
);
