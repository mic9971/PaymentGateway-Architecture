-- Local demo incremental schema migration. Safe to run after 001_payment_core.sql.
-- On existing Docker PostgreSQL volumes apply this file manually (init scripts run only once).
ALTER TABLE messaging.outbox_messages
    ADD COLUMN IF NOT EXISTS claim_token uuid,
    ADD COLUMN IF NOT EXISTS publish_attempts integer NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS last_publish_error text;

CREATE TABLE IF NOT EXISTS messaging.consumer_inbox (
    consumer_name text NOT NULL,
    event_id uuid NOT NULL,
    received_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (consumer_name, event_id)
);

CREATE TABLE IF NOT EXISTS messaging.audit_projection (
    consumer_name text NOT NULL,
    event_id uuid NOT NULL,
    event_type text NOT NULL,
    payload jsonb NOT NULL,
    received_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (consumer_name, event_id),
    FOREIGN KEY (consumer_name,event_id)
      REFERENCES messaging.consumer_inbox(consumer_name,event_id)
);

COMMENT ON TABLE messaging.audit_projection IS
    'Development-only idempotent read projection. NOT authoritative payment state.';
