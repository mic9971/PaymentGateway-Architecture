-- Local fake-provider ledger and inquiry scheduling. Do not use for real bank/PSP data.
-- The mock provider is stored under a separate schema for testability, but
-- shares the demo PostgreSQL server; this does NOT prove genuine remote-system HA.
CREATE SCHEMA IF NOT EXISTS mockpsp;
CREATE TABLE IF NOT EXISTS mockpsp.provider_operations (
    provider_operation_key text PRIMARY KEY,
    outcome text NOT NULL CHECK (outcome IN ('Succeeded','Failed')),
    recorded_at timestamptz NOT NULL DEFAULT now()
);
COMMENT ON TABLE mockpsp.provider_operations IS
    'Simulated external provider record: commit separately from gateway payment state.';

ALTER TABLE payment.payment_attempts
    ADD COLUMN IF NOT EXISTS next_inquiry_at timestamptz,
    ADD COLUMN IF NOT EXISTS inquiry_token uuid,
    ADD COLUMN IF NOT EXISTS inquiry_count integer NOT NULL DEFAULT 0;
CREATE INDEX IF NOT EXISTS ix_payment_unknown_inquiry
    ON payment.payment_attempts(next_inquiry_at, updated_at, attempt_id)
    WHERE state='Unknown';
