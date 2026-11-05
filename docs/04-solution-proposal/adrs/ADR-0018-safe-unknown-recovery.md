# ADR-0018 — Resolve Unknown PSP Outcomes via Inquiry, Not Payment Retry

- **Status:** Proposed; not approved for real PSPs.
- **Date:** 2026-11-05.
- **Related:** ADR-0015 (Unknown Provider Outcome), ADR-0017 (Outbox + RabbitMQ).
- **Owner:** Solution Architect / PSP Lead / Finance.

## Context

After attempting an external PSP operation, connection loss or worker termination may hide a completed external charge. Gateway's local DB state and provider financial state can diverge. Retrying payment initiation is unsafe without a provider contract.

## Options

A. Treat timeout as failure and create another attempt automatically — **Rejected**: may charge twice.

B. Wait indefinitely without inquiry — safe against extra charges but poor resolution, customer experience and operations.

C. **Candidate:** persist Unknown; schedule read-only status inquiry using a stable provider key; when authoritative evidence is available, apply a conditional Payment state transition + evidence + Outbox in one transaction. If inquiry remains inconclusive, keep Unknown and escalate.

## Implemented for local experiment

- `MockPspLedger` records simulated financial outcome in separate database transaction, deliberately returning Unknown after a simulated lost response.
- `NpgsqlUnknownRecoveryStore` leases inquiry requests with a unique token, max 5 tries, delay 30s.
- `PaymentRecoveryService` calls `QueryAsync` only, never `InitiateAsync`.
- Verified result updates Payment + Attempt + Outbox atomically.
- Missing mock provider record maps to Unresolved/Unknown, **not Failed**.
- Concurrency tests ensure one committed transition and outbox event.

## What the test does NOT establish

The mock ledger is in the same DB host and does not simulate provider legal finality, distinct PSP infrastructure, eventual statement availability, bank settlement, or provider-side idempotency retention. Real partner APIs may not support authoritative inquiry.

## Acceptance requirements

- PSP partner contract specifies stable idempotency reference, inquiry result semantics and signatures.
- Finance validates what `Succeeded` and `Failed` financially mean, including QR expiry and late transfers.
- Validate worker crash after PSP commit, replay, reconciliation delay, provider downtime, and competing webhook/inquiry outcomes.
- Operate a bounded Unknown escalation queue with accountable owner and audit.
- Verify RTO/RPO, security, supportability and provider availability.
- Review and sign off before accepting this ADR for production.

**Decision:** Proposed for mock education only; not accepted as a real-world PSP recovery design.
