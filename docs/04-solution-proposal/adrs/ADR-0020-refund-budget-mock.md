# ADR-0020 — RefundBudget Transactional Experiment, Not Production Acceptance

- **Status:** Proposed / experimental.
- **Decision owner:** Finance, Solution Architect, PSP Lead.
- **Related:** ADR-0013, ADR-0015, ADR-0018, ADR-0019.

## Problem

Two concurrent partial refunds for the same successful payment may both observe the same apparent remaining balance and over-authorize. A remote PSP response timeout can hide a completed refund; releasing the reservation or retrying via another provider can over-refund.

## Options

**A. Refund-owned row with atomic reservation (implemented in the Mock demo)**: Refund maintains \`verified_limit\`, \`reserved\` and \`completed\` amounts. Read and lock Payment eligibility within **one local PostgreSQL transaction** at creation. Atomically reserve with a conditional update; store Refund and Outbox intent in same transaction. Unknown retains reserved amount.

**B. Payment-owned reservation contract**: Payment exposes \`ReserveRefund\` as a command and Refund only handles external PSP operation. Strong single financial authorization authority but more context coordination and reservation lifecycle coupling.

**C. Query the Payment read model and check remaining funds in application code without atomic reservation:** Rejected; two concurrent requests can exceed the verified amount.

## Experimental choice

Implement A in a co-located Modular Monolith to **test the invariant**; the choice remains unapproved. Real financial entitlement may be materially different from Payment amount, depending on capture, settlement, disputes, FX, fees, reversal and PSP contract.

## Data/consistency contract

- Refund owns \`refund.*\` data and budget mutation. Payment remains payment state authority.
- Exception: in a single PG database, Refund obtains a read/lock of the Payment row while executing its local transaction; no Payment data is modified.
- \`RefundRequested\`, \`RefundSucceeded\`, \`RefundFailed\`, \`RefundUnknown\` are emitted via the outbox; messaging delivery is at least once.
- Stable Mock refund operation key is persisted before the simulated external call.
- Unknown never frees reservation without verified definitive no-effect evidence.
- Mock provider result is stored in a separate **local** transaction; it is not bank/PSP legal evidence.

## Verification

[32/32 CI tests, seven new refund integration cases](https://github.com/mic9971/PaymentGateway-Architecture/actions/runs/42153098364). Tests prove local concurrent reservation, idempotency, failure release, Unknown hold, inquiry resolution and duplicate-safe finalization.

## Approval gates

- Product/Finance define refundable entitlement and confirm multi-currency/fees, partial capture, disputes and settlement finality.
- PSP documents idempotent refund creation, status inquiry, provider keys and retentions.
- Security verifies authenticated merchants, tenant isolation, refund role restrictions and fraud/abuse limits.
- SRE validates failover, retained Unknown cases, disaster recovery, replay and operational escalation.
- Architect reviews cross-context exception and future migration to B or a dedicated reservation authority.

**No production acceptance; no automatic PR merge.**
