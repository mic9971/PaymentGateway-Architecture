# 05 — PostgreSQL Data Architecture, Correctness and Query Plans

## Operational source of truth

PostgreSQL stores **the gateway's operational state**, not necessarily bank account balances or the legal truth of final settlement. PSP/bank evidence, statements and contract authority must be modeled separately and reconciled.

## Candidate schemas

- merchant: merchants, credential metadata.
- payment: payments, payment_attempts, idempotency_records.
- routing: routing_rules, routing_decisions.
- integration: provider_correlations, provider_webhook_inbox.
- refund: refunds, refund_attempts.
- messaging: outbox_messages, inbox_receipts.
- reconciliation: runs, items, discrepancies.
- notification: webhook_deliveries.
- audit: audit_entries.

Each owning module writes only to its own schema; read models and contracts mediate cross-context access.

## Invariants and transaction boundaries

### Atomic payment creation
1. Authenticate and validate tenant, amount and currency.
2. Transactionally insert unique (merchant_id, operation, idempotency_key) with request payload hash.
3. On existing same-key + same-body operation, return prior logical result; on hash conflict, return a defined conflict response.
4. In the **same transaction**, create logical Payment + Attempt + Outbox message.
5. Commit, then return Pending/Accepted. PSP network operations are **outside** the database transaction.

### PSP attempt safety
The DB unique key **does not by itself** make an external PSP side effect exactly-once. Use stable PSP key when contract supports it and/or status inquiry/explicit recovery. After timeout never assume charge failed.

### Outbox & Inbox
Outbox in same transaction as state change. Publisher periodically claims rows using bounded concurrency and safe DB locking/leases, publishes with confirms, then updates published state. Delivery can occur twice; every consumer validates event identity and state transitions before side effects.

### Refund concurrency
Use conditional update, row lock or optimistic concurrency to ensure cumulative refundable reservation does not exceed verified available amount. Model Refund separately from Payment; distinguish Unknown refund outcome.

## Review-only SQL sketch (NOT production migration)

~~~sql
-- Concept only; ownership, key types, money units, partitioning and retention TBD.
CREATE SCHEMA IF NOT EXISTS payment;
CREATE TABLE payment.payments (
  payment_id uuid PRIMARY KEY,
  merchant_id uuid NOT NULL,
  amount_minor bigint NOT NULL CHECK (amount_minor > 0),
  currency char(3) NOT NULL,
  status text NOT NULL,
  version bigint NOT NULL DEFAULT 0,
  created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE payment.idempotency_records (
  merchant_id uuid NOT NULL,
  operation text NOT NULL,
  idempotency_key text NOT NULL,
  request_hash text NOT NULL,
  payment_id uuid,
  created_at timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (merchant_id, operation, idempotency_key)
);

CREATE INDEX ix_payment_merchant_recent
 ON payment.payments (merchant_id, created_at DESC, payment_id);

-- Example only: add payment attempts, outbox/inbox, refund, audit
-- and legal requirements before any deployment.
~~~

Do not copy this incomplete SQL to production. Define provider uniqueness, indexes for actual query patterns, FK ownership and status constraints after architecture review.

## Workload-aware indexing

| Query shape | Candidate index | Must verify |
|---|---|---|
| Merchant payment history by creation time | merchant_id, created_at DESC, payment_id | Pagination, buffer reads, p95 |
| Tenant-scoped payment lookup | merchant_id, payment_id | Compare index vs primary-key plan |
| Idempotency concurrent claim | unique merchant_id, operation, key | Race test, conflicting body |
| Provider callback reference | (provider_id, provider_ref) unique when contract supports | Provider uniqueness semantics |
| Unknown recovery scan | partial index on updated_at for UNKNOWN | Update overhead vs scan frequency |
| Outbox pending publisher | partial index by pending state/time | Claim contention, index churn |

Use PostgreSQL EXPLAIN (ANALYZE, BUFFERS), statistics/ANALYZE and pg_stat_statements to test actual workload. Each index adds write/storage overhead, so a large number of indexes is not automatically better.

## Connection pooling

Npgsql connection pool vs EF Core DbContext pooling are **different**. Estimate total simultaneous DB connections from all API + worker replicas, not a per-instance pool alone. Investigate DB lock waits, transaction length and query plans before raising maximum pool size.

## Cache / partitions

Redis cache can accelerate immutable-ish merchant/provider metadata, but it must not be the authoritative payment status, idempotency key claim or financial balance. Do not partition or shard upfront without retention, query, cardinality and growth evidence; partitioned unique constraints introduce additional key-design trade-offs.

## References

- https://www.postgresql.org/docs/current/indexes.html
- https://www.postgresql.org/docs/current/using-explain.html
- https://www.postgresql.org/docs/current/ddl-partitioning.html
- https://www.npgsql.org/doc/basic-usage.html
