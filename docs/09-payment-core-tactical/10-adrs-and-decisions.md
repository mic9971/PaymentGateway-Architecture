# 10 — Open ADRs and Design Trade-offs

## ADR-0014: PaymentAttempt as child entity vs independent aggregate

**Status:** Proposed / undecided.

### Option A — Attempt inside PaymentIntent aggregate
Benefits: centralized aggregate transition rules and simpler conceptual invariants. Costs: large aggregate load/contention as attempt volume grows; ambiguous independent lifecycle and worker locks.

### Option B — PaymentAttempt as separate aggregate (candidate)
Benefits: distinct provider side-effect lifecycle, durable claim/version and worker scaling. Costs: consistency between parent Payment and Attempt must be explicit; an attempt's state alone cannot authorize Payment financial finality.

**Candidate direction:** Option B for physical write model with a Payment-owned application service coordinating state changes; **not approved**. No PSP call or Routing decision outside Payment policy boundaries.

**Tests:** PC-T01/03/04/07/11. **Owner:** Architecture/Payment Lead.

## ADR-0015: Unknown PSP outcome resolution

**Status:** Proposed / PSP-dependent.

### Option A — Blind retry after timeout
**Reject** in payment charge flows: a lost response may follow successful financial side effect.

### Option B — Stable PSP idempotency key + inquiry (conditional preference)
Use only if PSP contract explicitly supports same-operation replay and query by stable operation/provider reference. Persist Unknown with evidence; inquiry first, and retry only when safe under contract.

### Option C — No safe automated recovery
Unknown persists, reconciliation/Finance/PSP escalation with explicit manual exception/approval. Required when PSP idempotency/inquiry cannot prove safety.

**Decision:** Never claim global exactly-once financial effects from PostgreSQL uniqueness, broker dedup, or local leases. The provider is an independent financial actor.

**Tests:** PC-T04/08/14/19. **Owner:** PSP Lead/Finance/Architect.

## ADR-0013 remains open — RefundBudget

See [ADR-0013](../04-solution-proposal/adrs/ADR-0013-refund-boundary-and-reservation.md). Payment and Refund may be separate Bounded Contexts yet have an atomic refundable entitlement constraint. Do not implement refund based on stale Payment projections.

## Revisit triggers

- Provider has no stable idempotent create, no authoritative inquiry or special late-transfer semantics.
- Financial custody changes to funds-management/ledger/payout business.
- Partial capture, FX, chargeback, dispute, settlement status impacts refund amount.
- PSP callback or bank statement contradicts a supposedly terminal Payment state.
- Measured traffic or team ownership warrants independently deployable services.

All decisions require **ADR rationale, responsible reviewers, counterexamples and failure-test evidence** before acceptance.
