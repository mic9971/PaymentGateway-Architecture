# ADR-0019 — Verified Webhook Handling, Idempotent Receipt and Reconciliation Ownership

- **Status:** Proposed; not accepted for production.
- **Date:** 2026-11-05.
- **Owners:** Architect, Security, PSP Lead and Finance.
- **Related:** ADR-0015, ADR-0017, ADR-0018, ADR-0013.

## Decision drivers

A provider callback can be forged, replayed, duplicated, delayed, or contradictory. The Gateway must not mark a financial payment successful merely because an unsigned HTTP request or a PSP API "accepted" response arrived.

## Proposed direction (Mock PSP only)

1. Validate provider HMAC over raw payload/timestamp **before** calling the state writer.
2. Use durable unique provider/event ID and reject correlation mismatches.
3. Apply Payment/Attempt transition + receipt + Outbox in **one PostgreSQL transaction**.
4. When a later signed callback contradicts a terminal status, **record evidence without changing state**; escalate to Finance/PSP/Operations under a defined correction process.
5. Reconciliation compares independent provider evidence with gateway state and stores discrepancies, **not direct Payment table corrections**.
6. Keep status inquiry **read-only**; no blind PSP charge retry on timeout.

## Alternatives

**A. Webhook directly writes Payment state without signature or dedup:** Rejected; impersonation, double side effects and regression.

**B. Save signed callback in durable Inbox, then process asynchronously:** Favored for complex real PSP integrations when acknowledgement/replay and throughput rules permit it; requires clear at-least-once contracts.

**C. Verify and apply in one short DB transaction after HTTP request:** Implemented local demo; easy to test, but callback processing latency and module coupling require review. No PSP network calls occur within transaction.

## Boundaries and technical debt

The local demo implementation lives in \`Payments.Infrastructure\` and writes the \`integration.mock_webhook_inbox\` table. This is an explicit **module ownership exception**, not a DDD best practice. Production should make Provider Integration own verified inbound receipts and Payment's public application command own financial transition. Atomicity is harder across independent deployables; revisit design with provider contracts and consistency evidence.

## Security limitations

The demo HMAC key is configured from local environment; no production secret manager, rotation/multiple active keys, IP allowlisting, mTLS, replay horizon retention or denial-of-service protections are implemented. Provider specs must determine actual signature validation.

## Evidence

- [25/25 GitHub Actions tests](https://github.com/mic9971/PaymentGateway-Architecture/actions/runs/42054657420) demonstrate six additional mock validator/DB/reconciliation behaviors.
- Full HTTP route tests and real PSP sandbox verification **Not Run**.
- No reconciliation with actual bank settlements or funds custody.

## Review/acceptance conditions

Product/Finance/Legal sign off funds-flow and finality semantics; PSP confirms signed callback scheme and stable event correlation; Security approves secret handling; Architect validates ownership and test coverage; QA executes HTTP, concurrency/replay and provider sandbox tests; SRE runs game days and metrics/alerting. **Do not mark Accepted before these conditions.**
