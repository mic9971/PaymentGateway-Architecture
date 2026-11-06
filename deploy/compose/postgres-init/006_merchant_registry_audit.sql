-- Development-only merchant registry: no public onboarding/admin API.
CREATE SCHEMA IF NOT EXISTS iam;

CREATE TABLE IF NOT EXISTS iam.merchants (
    merchant_id uuid PRIMARY KEY,
    status text NOT NULL CHECK(status IN ('Active','Suspended','Closed')),
    updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS iam.merchant_clients (
    merchant_id uuid NOT NULL REFERENCES iam.merchants(merchant_id),
    issuer text NOT NULL,
    subject text NOT NULL,
    client_id text NOT NULL,
    granted_scopes text[] NOT NULL,
    enabled boolean NOT NULL DEFAULT true,
    updated_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY(merchant_id,issuer,subject,client_id),
    UNIQUE(issuer,subject,client_id)
);

CREATE TABLE IF NOT EXISTS iam.access_audit (
    audit_id uuid PRIMARY KEY,
    timestamp_utc timestamptz NOT NULL DEFAULT now(),
    request_id text NOT NULL,
    merchant_id uuid,
    subject text,
    client_id text,
    method text NOT NULL,
    request_path text NOT NULL,
    http_status integer NOT NULL,
    CHECK (http_status BETWEEN 100 AND 599)
);
CREATE INDEX IF NOT EXISTS ix_iam_access_audit_time
    ON iam.access_audit(timestamp_utc DESC);
COMMENT ON TABLE iam.access_audit IS
    'Best-effort HTTP security telemetry, NOT transactional financial audit or tamper-proof retention.';
