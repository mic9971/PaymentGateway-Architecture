-- Local-only refund design: NO real PSP and no production financial ledger.
CREATE SCHEMA IF NOT EXISTS refund;
CREATE TABLE IF NOT EXISTS refund.refund_budgets (
  payment_id uuid PRIMARY KEY REFERENCES payment.payment_intents(payment_id),
  verified_limit_minor bigint NOT NULL CHECK (verified_limit_minor > 0),
  reserved_minor bigint NOT NULL DEFAULT 0 CHECK (reserved_minor >= 0),
  completed_minor bigint NOT NULL DEFAULT 0 CHECK (completed_minor >= 0),
  CHECK (reserved_minor + completed_minor <= verified_limit_minor)
);
CREATE TABLE IF NOT EXISTS refund.refund_requests (
  refund_id uuid PRIMARY KEY,
  payment_id uuid NOT NULL REFERENCES payment.payment_intents(payment_id),
  merchant_id uuid NOT NULL,
  amount_minor bigint NOT NULL CHECK (amount_minor > 0),
  currency char(3) NOT NULL,
  idempotency_key text NOT NULL,
  request_hash bytea NOT NULL,
  provider_operation_key text NOT NULL UNIQUE,
  state text NOT NULL CHECK (state IN ('Requested','Processing','Succeeded','Failed','Unknown')),
  claim_id uuid,
  lease_until timestamptz,
  inquiry_token uuid,
  next_inquiry_at timestamptz,
  inquiry_count integer NOT NULL DEFAULT 0,
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now(),
  UNIQUE (merchant_id,idempotency_key)
);
CREATE INDEX IF NOT EXISTS ix_refund_ready
 ON refund.refund_requests(created_at,refund_id) WHERE state='Requested';
CREATE INDEX IF NOT EXISTS ix_refund_recovery
 ON refund.refund_requests(next_inquiry_at,refund_id) WHERE state='Unknown';

-- A fake provider ledger, committed separately from gateway refund updates,
-- allows tests to simulate response loss after a remote effect.
CREATE TABLE IF NOT EXISTS refund.mock_provider_refunds (
  provider_operation_key text PRIMARY KEY,
  outcome text NOT NULL CHECK (outcome IN ('Succeeded','Failed')),
  recorded_at timestamptz NOT NULL DEFAULT now()
);
COMMENT ON TABLE refund.refund_budgets IS
 'MVP option A: Refund-owned atomic reservation. Approved by neither Finance nor architecture board.';
