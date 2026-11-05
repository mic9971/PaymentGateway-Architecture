-- Development-only signed mock PSP callback evidence and reconciliation read model.
-- Not a production webhook schema or settlement ledger.
CREATE SCHEMA IF NOT EXISTS integration;
CREATE SCHEMA IF NOT EXISTS reconciliation;
CREATE TABLE IF NOT EXISTS integration.mock_webhook_inbox (
    provider_id text NOT NULL,
    event_id uuid NOT NULL,
    payment_id uuid NOT NULL,
    attempt_id uuid NOT NULL,
    provider_operation_key text NOT NULL,
    payload_hash bytea NOT NULL,
    outcome text NOT NULL CHECK (outcome IN ('Succeeded','Failed')),
    disposition text NOT NULL CHECK (disposition IN
        ('Received','Applied','AlreadyFinal','ConflictingFinal')),
    received_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (provider_id,event_id)
);
CREATE INDEX IF NOT EXISTS ix_mock_webhook_payment
 ON integration.mock_webhook_inbox (payment_id,received_at DESC);

CREATE TABLE IF NOT EXISTS reconciliation.mock_discrepancies (
    provider_operation_key text PRIMARY KEY,
    payment_id uuid NOT NULL,
    provider_outcome text NOT NULL,
    gateway_outcome text NOT NULL,
    reason text NOT NULL,
    detected_at timestamptz NOT NULL DEFAULT now(),
    observed_at timestamptz NOT NULL DEFAULT now(),
    resolved_at timestamptz
);
COMMENT ON TABLE integration.mock_webhook_inbox IS
  'Verified demo HMAC callback receipts, deduped. Not a true PSP evidence store.';
