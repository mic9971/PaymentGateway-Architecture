# 05 — PostgreSQL Schema and Durable Idempotency

**Status: review-level SQL sketches. NOT a complete tested production migration.**

## Data ownership and DB transaction

Payment owns \`payment_intents\`, \`payment_attempts\`, \`idempotency_records\`, \`payment_evidence\` and \`payment_state_history\`. Messaging Outbox is shared **technical infrastructure** but inserted in the same local transaction as Payment business state. Provider callback receipts belong to Provider Integration.

~~~mermaid
flowchart TB
  Request["Merchant create request"] --> Tx["PostgreSQL TX"]
  Tx --> Idem["Unique Idempotency Record"]
  Tx --> Intent["PaymentIntent"]
  Tx --> Attempt["PaymentAttempt"]
  Tx --> Outbox["OutboxMessage"]
  Tx --> Commit["COMMIT"]
  Commit --> Response["202 Pending / stable PaymentId"]
  Outbox --> Pub["Background outbox publisher"]
  Pub --> Rabbit["RabbitMQ"]
  Rabbit --> Worker["Payment Worker"]
  Worker --> PSP["PSP (outside DB TX)"]
~~~

## Candidate SQL DDL (trimmed)

~~~sql
-- Draft only; constraints, permissions, indexes and lifecycle remain to review.
CREATE SCHEMA IF NOT EXISTS payment;

CREATE TABLE payment.payment_intents (
  payment_id uuid PRIMARY KEY,
  merchant_id uuid NOT NULL,
  amount_minor bigint NOT NULL CHECK (amount_minor > 0),
  currency char(3) NOT NULL,
  state text NOT NULL CHECK (state IN
    ('Created','Pending','Processing','RequiresAction',
     'Succeeded','Failed','Unknown','ExpiredPendingReview','Investigation')),
  version bigint NOT NULL DEFAULT 0,
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE payment.idempotency_records (
  merchant_id uuid NOT NULL,
  operation text NOT NULL,
  idempotency_key text NOT NULL,
  request_hash bytea NOT NULL,
  payment_id uuid, -- populated before create transaction commits
  created_at timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (merchant_id,operation,idempotency_key),
  FOREIGN KEY (payment_id) REFERENCES payment.payment_intents(payment_id)
);

CREATE TABLE payment.payment_attempts (
  attempt_id uuid PRIMARY KEY,
  payment_id uuid NOT NULL REFERENCES payment.payment_intents(payment_id),
  provider_id text NOT NULL,
  provider_key text NOT NULL,
  state text NOT NULL,
  claim_generation bigint NOT NULL DEFAULT 0,
  lease_until timestamptz,
  last_error_class text,
  created_at timestamptz NOT NULL DEFAULT now(),
  UNIQUE(payment_id,attempt_id)
);

CREATE INDEX ix_payment_merchant_recent
  ON payment.payment_intents(merchant_id,created_at DESC,payment_id);
~~~

**Notice:** \`payment_id\` in the idempotency record is nullable in the draft because the unique claim is inserted before the Payment row inside a single local transaction. The application must **never commit a successful CreatePayment claim without linking the PaymentId**, and should enforce this with robust transaction handling/tests or a refined schema/procedure. If another request sees the committed idempotency record, it must retrieve the existing Payment and compare the request fingerprint. This is not an excuse to leave permanent incomplete claims.

## Atomic create algorithm

1. Begin short PostgreSQL transaction.
2. Attempt \`INSERT INTO payment.idempotency_records (...) VALUES (...) ON CONFLICT DO NOTHING RETURNING ...\`.
3. **Winner** inserts PaymentIntent, initial PaymentAttempt and Outbox intent; attaches PaymentId to claimed row; commits transaction.
4. **Loser** reads committed record after conflict coordination; compares request hash; returns same payment result if identical, otherwise conflict. Do not open a second new payment.
5. PSP network call happens **after** commit, from background worker under its own recovery contract.
6. Both successful create and duplicate replay return stable logical PaymentId.
7. Outbox publisher may publish twice after a crash; consumers must deduplicate.

PostgreSQL uniqueness and \`ON CONFLICT\` arbitrate competing inserts: https://www.postgresql.org/docs/current/sql-insert.html

## Conditional transition sketch

~~~sql
UPDATE payment.payment_intents
SET state = @next_state,
    version = version + 1,
    updated_at = now()
WHERE payment_id = @payment_id
  AND merchant_id = @merchant_id
  AND version = @expected_version
  AND state = ANY(@allowed_prior_states)
RETURNING payment_id, state, version;
~~~

Apply evidence/audit/outbox in the **same transaction** as a successful state change. Zero updated rows means concurrent change or disallowed transition; do not blindly retry without inspecting current state and evidence.

## PSP attempt claim and stale lease trap

Worker attempts may be durably claimed using a conditional \`UPDATE ... RETURNING\`. However, after lease expiry a second worker cannot automatically assume PSP was never called. It must follow PSP idempotency/status inquiry policy, or escalate Unknown.

## Index and query plan evidence

- Check actual query filters, buffer reads and plans before adding indexes.
- Candidate indexes: merchant payment history, provider reference correlation, Unknown recovery scan, pending outbox scan.
- Keep uniqueness aligned with real PSP reference scope (provider + PSP merchant account may be needed).
- \`SKIP LOCKED\` is suitable for queue-like outbox claim jobs, **not** for general consistent reads.

PostgreSQL locking: https://www.postgresql.org/docs/current/explicit-locking.html  
PostgreSQL SELECT/locking: https://www.postgresql.org/docs/current/sql-select.html

## Open design details

Exact transaction isolation, retention, FK/tenant boundary, RLS vs app authorization, provider reference uniqueness, migration constraints, backup/RPO and status-finality semantics must be resolved before implementation.
