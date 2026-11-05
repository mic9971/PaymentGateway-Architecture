# ADR-0013 — Payment vs Refund Consistency Boundary

- **Status:** Proposed / Needs Finance and PSP review.
- **Context:** Multi-merchant payment gateway must support concurrent partial refunds without exceeding the verified refundable amount. Payment and Refund are logical Bounded Context candidates. External PSP refund may timeout after financial side effect.
- **Business invariant:** \`CompletedRefunds + ReservedInFlightRefunds <= VerifiedRefundableEntitlement\` for the same payment, considering provider policy, reversals and business currency units.

## Option A — Refund-owned RefundBudget (tentative preference)

Refund stores a single guard row per PaymentId: verified entitlement, reserved, completed, version. Authorized evidence from Payment initializes/updates the ceiling under a documented protocol. Within **one PostgreSQL transaction**, conditional update/row lock reserves requested amount only if within available limit; then creates RefundAttempt and Outbox. Refund worker contacts PSP outside DB transaction. Do not release reservation merely on PSP timeout; keep UNKNOWN and reconcile.

**Benefits:** Refund handles financial reservation invariant, clear local consistency with Refund lifecycle, simpler independent refund operational ownership.

**Costs/risks:** Must prove entitlement projection freshness, payment corrections and money-rule changes cannot accidentally raise limit. Requires coordination contract and provenance. Need multi-currency/partial capture rules validated by Finance.

## Option B — Payment-owned refundable reservation

Payment owns the authoritative refundable budget and offers atomic \`ReserveRefund\` command returning immutable ReservationId. Refund context owns workflow/PSP execution after successful reservation. Completion/failure/reconciliation updates reservation through controlled commands.

**Benefits:** Single financial authorization point near Payment state, less independent stale entitlement risk.

**Costs/risks:** Tight Payment ↔ Refund workflow coupling and two lifecycles; request/PSP/receipt transactions must avoid double-spend or stranded reservations.

## Rejected anti-pattern

Refund reads \`Payment.Succeeded\` and remaining amount once, then sends PSP request without an atomic reservation. Two concurrent refunds can jointly exceed entitlement. Redis lock or eventual projection alone is not sufficient.

## Open questions

- Who defines refundable entitlement, and does it equal paid/captured/settled amount?
- Partial capture, void/reversal, FX fees, dispute/chargeback impact?
- Does PSP support refund idempotency and status inquiry?
- How long do Unknown refund reservations persist and who can resolve them?
- Which module owns final "refund processed" evidence and financial reconciliation?

## Decision / implementation gate

**Not accepted.** Finance/PSP/Product/Architect review, then write concurrency integration test and PSP crash/timeout/resume tests before implementing migration or external refund. Recorded on DDD Decision Gate G-DDD-05, G-DDD-14.
