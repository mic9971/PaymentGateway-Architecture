# Context Map — Explicit Integration Relationships

Direction of arrows = caller/event producer → receiver. It does **not automatically** mean the caller is upstream in the DDD power relationship; business data authority is documented in the table.

~~~mermaid
flowchart TB
    Merchant["Merchant BC"] -->|MerchantProfile query contract| Payment["Payment BC"]
    Payment -->|SelectProvider| Routing["Routing BC"]
    Payment -->|ExecuteAttempt| Provider["Provider Integration BC / ACL"]
    Provider -->|VerifiedProviderOutcome| Payment
    Refund["Refund BC"] -->|RefundableEvidence contract| Payment
    Refund -->|ExecuteRefund| Provider
    Recon["Reconciliation BC"] -->|PSP inquiry + statements| Provider
    Recon -->|ApplyVerifiedOutcome (controlled)| Payment
    Payment -->|PaymentStatusChanged event| Notify["Notifications BC"]
    Refund -->|RefundStatusChanged event| Notify
    Payment -.->|Business evidence| Ops["Operations BC candidate"]
    Recon -.->|Investigation evidence| Ops
~~~

## Contract decisions

| ID | Caller / producer | Receiver | Contract | Pattern | Ownership and correctness |
|---|---|---|---|---|---|
| CM-01 | Payment | Merchant | MerchantProfile | Sync query | Merchant is authoritative for its own identity/policy; Payment no writes |
| CM-02 | Payment | Routing | SelectProvider | Sync policy | Routing owns eligibility, policies and decision trace |
| CM-03 | Payment | Provider Integration | ExecuteAttempt | Async command / worker | Provider ACL maps PSP semantics; no duplicate financial side effect |
| CM-04 | Provider Integration | Payment | VerifiedProviderOutcome | Inbox / application service | Payment validates transition; signature and correlation already checked |
| CM-05 | Refund | Payment | RefundableEvidence | Query contract | Refund budget must not rely on stale unguarded data |
| CM-06 | Refund | Provider Integration | ExecuteRefund | Async command | Ambiguous refund remains UNKNOWN |
| CM-07 | Reconciliation | Provider Integration | QueryStatus / ProviderStatement | Sync/batch | External financial evidence sourced with provenance |
| CM-08 | Reconciliation | Payment | ApplyVerifiedOutcome | Controlled command | Payment validates final transition and audits correction |
| CM-09 | Payment | Notifications | PaymentStatusChanged | Outbox → RabbitMQ | At-least-once; stable event ID; no assumed ordering |
| CM-10 | Refund | Notifications | RefundStatusChanged | Outbox → RabbitMQ | At-least-once, dedup by event+consumer |
| CM-11 | Domain contexts | Operations | BusinessEvidenceRecorded | Append-only/event | Ops investigate; never direct Payment table writes |

## Upstream / downstream and DDD patterns

- **Merchant → Payment:** Merchant provides a published contract; Payment is downstream of Merchant master-data authority.
- **PSP external system → Provider Integration:** Provider Integration acts as an **Anti-Corruption Layer (ACL)** between third-party PSP models and the Payment domain.
- **Payment → Notifications:** Notifications is downstream of Payment integration events; it doesn't own the Payment state.
- **Payment ↔ Reconciliation:** Reconciliation reads Payment evidence and PSP evidence, but **Payment remains the authoritative writer** for its own state.
- **Payment ↔ Refund:** A strong financial invariant crosses business meanings. Need explicit consistency design rather than assume eventual consistency is sufficient.

## Rules for APIs and events

- Public contracts versioned; private EF entities/repositories never exposed across BC boundaries.
- Events include \`event_id\`, \`version\`, \`occurred_at\`, \`correlation_id\`, domain IDs and minimal payload; sensitive data redacted.
- RabbitMQ may redeliver and reorder; each consumer has durable dedup/transition guards.
- PSP request ID/idempotency key remains stable for safe repeat **only when the PSP contract supports it**.
- Cross-context sync calls need defined timeout, error classification and fallback; synchronous dependency does not create a distributed ACID guarantee.

## Context Map change triggers

Document a new ADR when a shared business language splits, a financial invariant forces coupling, provider integration diversifies enough to need independent lifecycle, or deployment/team ownership changes.
