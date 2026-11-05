# 02 — Strategic DDD: Capabilities, Bounded Contexts and Ownership

## Capabilities → bounded contexts (working hypotheses)

| Capability | Context | Core vs supporting | Owns data |
|---|---|---|---|
| Merchant onboarding and credentials | Merchant | Supporting | merchant settings, credential metadata |
| Create/track payment; attempt lifecycle | Payment | Core | payments, attempts, idempotency |
| Provider selection policies | Routing | Supporting/Core TBD | routing rules and decision evidence |
| Provider API, webhook validation, inquiry | Provider Integration | Supporting | PSP references and inbox |
| Full and partial refunds | Refund | Supporting | refund/attempts |
| Status/settlement comparison and discrepancies | Reconciliation | Core/Supporting TBD | runs, items, investigations |
| Notify merchants | Notifications | Generic | delivery receipts |
| Financially relevant audit / investigation | Audit & Ops | Supporting | audit events, ops cases |

Bounded contexts do NOT equal separately deployed microservices. An initial Modular Monolith can implement these as modules and schemas.

## Context map

~~~mermaid
flowchart TD
    Merchant["Merchant Context"] -->|Merchant identity + settings contract| Payment["Payment Context"]
    Payment -->|Select allowed provider| Routing["Routing Context"]
    Routing -->|Routing policy| Provider["Provider Integration Context"]
    Payment -->|Provider invocation and normalized outcome| Provider
    Provider -->|Authenticated callback, inquiry result| Payment
    Payment -->|Verified payment state| Refund["Refund Context"]
    Payment -->|Payment/attempt evidence| Recon["Reconciliation Context"]
    Provider -->|PSP statement / inquiry| Recon
    Payment -->|Domain event| Notification["Notifications"]
    Refund -->|Refund event| Notification
    Payment --> Audit["Audit / Operations"]
    Recon --> Audit
~~~

## Integration relationship and contracts

- Merchant context provides merchant identity/permissions; Payment may NOT update Merchant private tables.
- Payment owns payment state and decides valid transitions; provider adapter maps PSP semantics, NOT internal business policy.
- Routing chooses eligible PSP and records evidence; automatic retry across PSP after unknown outcome is **prohibited until independently proven safe**.
- Refund owns refund requests; read verified payment state via contracts. Ensure total refunded amount cannot exceed allowed sum under concurrency.
- Reconciliation compares independent provider/bank evidence to gateway records; cannot just copy arbitrary PSP state into Payment without review.
- Notifications owns delivery status, not payment truth.
- Audit retains provenance without becoming a substitute for the financial source of truth.

## Modeling workshop

1. Walk through actual money-flow and success/failure business language.
2. Identify events and aggregates: PaymentIntent, PaymentAttempt, ProviderEvent, Refund, RefundAttempt, Discrepancy.
3. Write business invariants per aggregate, including UNKNOWN and late payment.
4. Draw aggregate transaction boundaries and explicit integration contracts.
5. Assign one owner per write model and verify no cross-schema writes.
6. Review against fraud/security/compliance boundary and PSP contract.

## Team/ownership note

Team names in workbook are placeholders. Assign actual owners and cognitive load, review coupling monthly and preserve ownership independent of technology stacks.
