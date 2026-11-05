-- DEVELOPMENT FOUNDATION ONLY. Not a production-grade online migration system.
-- Applied automatically by Postgres container on an EMPTY data volume.
CREATE SCHEMA IF NOT EXISTS payment;
CREATE SCHEMA IF NOT EXISTS messaging;

CREATE TABLE IF NOT EXISTS payment.payment_intents (
    payment_id uuid PRIMARY KEY,
    merchant_id uuid NOT NULL,
    merchant_reference text NOT NULL CHECK (length(merchant_reference) BETWEEN 1 AND 100),
    amount_minor bigint NOT NULL CHECK (amount_minor > 0),
    currency char(3) NOT NULL,
    payment_method text NOT NULL CHECK (payment_method = 'MOCK'),
    state text NOT NULL CHECK (state IN ('Pending', 'Processing', 'RequiresAction', 'Succeeded', 'Failed', 'Unknown')),
    version bigint NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_payment_by_merchant_created
    ON payment.payment_intents(merchant_id, created_at DESC, payment_id);

CREATE TABLE IF NOT EXISTS payment.idempotency_records (
    merchant_id uuid NOT NULL,
    operation text NOT NULL,
    idempotency_key text NOT NULL,
    request_hash bytea NOT NULL,
    payment_id uuid NOT NULL REFERENCES payment.payment_intents(payment_id)
        DEFERRABLE INITIALLY DEFERRED,
    created_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (merchant_id, operation, idempotency_key)
);

CREATE TABLE IF NOT EXISTS payment.payment_attempts (
    attempt_id uuid PRIMARY KEY,
    payment_id uuid NOT NULL REFERENCES payment.payment_intents(payment_id),
    provider_id text NOT NULL,
    provider_operation_key text NOT NULL UNIQUE,
    state text NOT NULL CHECK (state IN ('Pending', 'Processing', 'Succeeded', 'Failed', 'Unknown')),
    claim_id uuid,
    lease_until timestamptz,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_pending_attempts
    ON payment.payment_attempts(created_at, attempt_id)
    WHERE state = 'Pending';

CREATE INDEX IF NOT EXISTS ix_expired_attempts
    ON payment.payment_attempts(lease_until, attempt_id)
    WHERE state = 'Processing';

CREATE TABLE IF NOT EXISTS messaging.outbox_messages (
    event_id uuid PRIMARY KEY,
    aggregate_id uuid NOT NULL,
    event_type text NOT NULL,
    payload jsonb NOT NULL,
    occurred_at timestamptz NOT NULL,
    published_at timestamptz,
    claim_until timestamptz
);

CREATE INDEX IF NOT EXISTS ix_outbox_unpublished
    ON messaging.outbox_messages(occurred_at, event_id)
    WHERE published_at IS NULL;

COMMENT ON TABLE messaging.outbox_messages IS
    'Outbox persistence only in v0.1. RabbitMQ publisher will be added and tested in a separate PR.';

