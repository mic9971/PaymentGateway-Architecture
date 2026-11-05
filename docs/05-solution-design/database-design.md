# PostgreSQL Data Design — Proposal

Candidates: merchants, payments, payment_attempts, idempotency_records, refunds, provider_events_inbox, outbox_messages, reconciliation_runs, webhook_deliveries, audit_entries.

Rules:
- Composite unique key for (merchant_id, idempotency_key) scoped by operation.
- Payment attempt/state changes must respect invariants and concurrency.
- DB transaction never wraps external PSP network call.
- Partitioning, indexes and connection pool size require actual workload/query-plan evidence.

ERD, schema ownership, constraints and retention: TBD.
