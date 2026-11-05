# 01 — Scope, Requirements and Assumptions

## User story (business first)

"As a merchant, I can initiate a payment through our gateway, receive the instruction needed by the payer (e.g., QR or wallet redirect), and later learn a trustworthy, auditable outcome even when the network or PSP fails."

This is a **proposed** story inferred from the original customer request. Product Owner acceptance remains missing.

## Payment BC responsibilities

**Own:** logical payment identity, merchant ownership, amount/currency, state machine and transition guards, PaymentAttempt records, idempotency ledger, payment outcome evidence and auditable transitions.

**Does not own:**
- PSP-specific protocol/security/error taxonomy: **Provider Integration / ACL**.
- Eligible PSP selection and rule version: **Routing**.
- Provider reconciliation, discrepancy case management: **Reconciliation**.
- Refund attempt lifecycle and its budget, pending ADR-0013: **Refund (or Payment reservation contract)**.
- Delivery to merchant webhook: **Notifications**.
- Legal bank balance, custody or settlement: **TBD outside current orchestration scope**.

## Required functional requirements

| ID | Command/feature | Acceptance sketch | Status |
|---|---|---|---|
| FR-PAY-001 | Create payment with Idempotency-Key | Same merchant/key/body returns same logical result | Proposed |
| FR-PAY-002 | Get payment by ID | Correct merchant only, stable status meanings | Proposed |
| FR-PAY-003 | Track PSP attempt | Stable attempt reference; crash recovery | Proposed |
| FR-PAY-004 | Record verified outcome | Only accepted transitions after authenticated evidence | Proposed |
| FR-PAY-005 | Reconcile UNKNOWN | Inquiry or Finance escalation; no blind re-charge | Proposed |
| FR-PAY-006 | Emit merchant payment events | State+Outbox atomic, notifications at least once | Proposed |

## Quality Attribute Scenarios

| Stimulus | Expected behavior | Evidence |
|---|---|---|
| Two POSTs with same key arrive simultaneously | One logical PaymentIntent and intended PSP side effect | PC-T01 + PSP proof |
| Worker crashes after PSP accepted charge | No blind retry; UNKNOWN resolved by authoritative PSP evidence | PC-T04 |
| PSP sends signed webhook before API response | Correlated, valid final transition without state regression | PC-T07 |
| PSP sends duplicate/forged callbacks | Duplicate ignored; forgery rejected; audit retained | PC-T09 |
| RabbitMQ down after DB commit | Outbox retains durable publish intent | PC-T10 |
| Merchant A guesses B's payment ID | No cross-merchant read/update | PC-T12 |
| Late QR payment after expiry | Investigation/Finance policy; not arbitrary Failed | PC-T14 |

Targets for p95, availability, RTO/RPO and Unknown-resolution time **remain TBD**, not invented.

## Business assumptions and blockers

- No funds custody has been approved by Sponsor/Legal/Finance.
- PSP payment finality is **provider-specific**.
- One stable idempotency key per *logical payment initiation* is the candidate API behavior; time horizon and retry policy TBD.
- Multi-tenant merchant IDs must be derived from authentication, not trusted solely from client request body.
- Multiple PSP integration is requested, but provider names/contracts still TBD.
- .NET 8 is a **learning stack preference**; Microsoft support ends in November 2026; choose supported production LTS before go-live.

**Exit Gate:** Sponsor/Product/PSP/Finance approve scope and semantics before actual money-moving provider calls.
