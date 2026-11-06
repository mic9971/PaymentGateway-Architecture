-- Development-only two-person refund approval and transactional financial event journal.
-- The threshold of 1,000,000 VND is candidate policy, NOT approved by Finance.
-- Append-only via trigger for ordinary SQL users; DB owner/superuser may bypass:
-- this is NOT tamper-proof, WORM, signed or compliance-certified storage.

ALTER TABLE refund.refund_requests
  DROP CONSTRAINT IF EXISTS refund_requests_state_check;
ALTER TABLE refund.refund_requests
  ADD CONSTRAINT refund_requests_state_check
  CHECK (state IN ('PendingApproval','Requested','Processing','Succeeded','Failed','Unknown'));

ALTER TABLE refund.refund_requests
  ADD COLUMN IF NOT EXISTS approval_required boolean NOT NULL DEFAULT false,
  ADD COLUMN IF NOT EXISTS request_actor_subject text,
  ADD COLUMN IF NOT EXISTS request_actor_client text;
ALTER TABLE refund.refund_requests
  ADD CONSTRAINT refund_request_approval_actor_check
  CHECK (NOT approval_required OR
         (request_actor_subject IS NOT NULL AND length(request_actor_subject)>0
          AND request_actor_client IS NOT NULL AND length(request_actor_client)>0));

CREATE TABLE IF NOT EXISTS refund.refund_approvals (
  refund_id uuid NOT NULL REFERENCES refund.refund_requests(refund_id),
  merchant_id uuid NOT NULL,
  actor_subject text NOT NULL,
  actor_client text NOT NULL,
  issuer text NOT NULL,
  decision text NOT NULL CHECK (decision='Approved'),
  recorded_at timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (refund_id, actor_subject)
);

CREATE SCHEMA IF NOT EXISTS finance_audit;
CREATE TABLE IF NOT EXISTS finance_audit.refund_actions (
  event_id uuid PRIMARY KEY,
  refund_id uuid NOT NULL,
  payment_id uuid NOT NULL,
  merchant_id uuid NOT NULL,
  actor_subject text,
  actor_client text,
  action_kind text NOT NULL CHECK (action_kind IN
     ('RefundRequested','RefundPendingApproval','RefundApprovalRecorded','RefundReleased')),
  amount_minor bigint NOT NULL CHECK(amount_minor > 0),
  occurred_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_finance_audit_refund_actions
  ON finance_audit.refund_actions(refund_id,occurred_at,event_id);

CREATE OR REPLACE FUNCTION finance_audit.reject_mutation() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
  RAISE EXCEPTION 'refund financial action audit is insert-only';
END;
$$;
DROP TRIGGER IF EXISTS trg_refund_audit_insert_only ON finance_audit.refund_actions;
CREATE TRIGGER trg_refund_audit_insert_only
  BEFORE UPDATE OR DELETE ON finance_audit.refund_actions
  FOR EACH ROW EXECUTE FUNCTION finance_audit.reject_mutation();

COMMENT ON TABLE finance_audit.refund_actions IS
  'Demo insert-only transactional refund action evidence; NOT a certified immutable ledger.';
