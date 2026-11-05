# Bounded Context Catalog v1.1

**Proposed logical contexts:** 8. **Not** eight microservices. All statuses here = Proposed unless specifically marked Needs Confirmation.

| ID | BC | Proposed subdomain | Owns business rules and authoritative writes | Public capability |
|---|---|---|---|---|
| BC-01 | Merchant | Supporting | Merchant onboarding, credentials metadata, merchant policy | MerchantProfile, IsMerchantEnabled |
| BC-02 | Payment | **Core** | PaymentIntent, PaymentAttempt, idempotency, transition validation | CreatePayment, GetPayment, PaymentStatusChanged |
| BC-03 | Routing | Supporting or Core **TBD** | Routing policies and decisions, eligible PSP selection | SelectProvider, RoutingDecision |
| BC-04 | Provider Integration | Supporting; **ACL** | Contract translation for PSP/VietQR/wallet, verified callbacks, provider correlation and inquiry | ExecuteAttempt, NormalizeProviderOutcome |
| BC-05 | Refund | Supporting | RefundRequest, RefundAttempt, refundable reservation invariant | RequestRefund, RefundStatusChanged |
| BC-06 | Reconciliation | Core or Supporting **TBD** | Statement matching, unknown-outcome investigation, discrepancy resolution | ReconcilePayment, VerifiedOutcomeEvidence |
| BC-07 | Notifications | Generic | Merchant delivery, retries, replay and receipts | DeliverMerchantEvent, DeliveryResult |
| BC-08 | Audit & Operations | Supporting **boundary TBD** | Investigation cases, privileged operations and evidence links | OpenInvestigation, RecordOperatorAction |

## Distinguish the contexts precisely

### Payment vs Provider Integration
Payment state belongs to **Payment**. Provider Integration knows PSP-specific status names, credential/signature requirements, timeouts, provider references and quirks. It maps a provider answer into a normalized result with **definitive/ambiguous evidence**. It does not unilaterally mark a Payment "Succeeded".

### Payment vs Routing
Payment asks "which provider is eligible for this logical payment attempt?" Routing returns a decision with policy version and evidence. Routing MUST NOT authorize a second PSP side effect when a prior PSP attempt is UNKNOWN unless Finance, Product and PSP contracts prove this is safe.

### Payment vs Refund
Payment knows verified amount/state; Refund knows refund requests, reservations and refund attempts. They can be logically separate yet need a carefully designed **atomic limit enforcement mechanism**. At MVP, co-locating transactional execution may be safer than asynchronous inter-service reservations; see [Aggregates & Invariants](./03-aggregates-and-invariants.md) and proposed ADR-0013.

### Payment vs Reconciliation
Payment contains the current gateway state. Reconciliation compares **independent bank/PSP evidence** and opens cases or issues a controlled correction command to Payment. It does not change the Payment table directly.

### Audit vs Operations
Audit emission/storage is typically **cross-cutting infrastructure**, not a distinct bounded context by itself. Operations case management has business terms ("case", "assignment", "verified resolution", "approval") and may deserve a context. Keep the combined candidate under review rather than prematurely treat logging as its own domain.

## Why only these contexts?

There is no demonstrated need yet for separate Ledger, Settlement, Payout, Fraud, Disputes, Subscription Billing, PCI Vault, Reporting or Pricing BCs. They are **possible future domains** conditional on financial responsibility, regulator/legal scope and MVP product requirements. Do not scope them in silently.

## What can become a separate deployable service later?

Only after measurable drivers: separate scaling SLO, independent team/release cadence, workload/security isolation and acceptable distributed-correctness cost. Mapping a context to a service is a **deployment architecture decision**, not DDD's definition.
