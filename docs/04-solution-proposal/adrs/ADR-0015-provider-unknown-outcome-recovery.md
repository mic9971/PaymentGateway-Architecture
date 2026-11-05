# ADR-0015 — PSP Timeout, Unknown Outcome and Safe Recovery

- **Status:** Proposed / PSP contract dependent / not accepted.
- **Date:** 2026-11-05
- **Decision owners:** PSP Lead, Finance, Product, Solution Architect.
- **Affected contexts:** Payment, Provider Integration, Routing, Reconciliation, Refund.

## Problem statement

A successful PSP financial side effect can occur even when Gateway receives a timeout or the worker dies before persisting the response. Retrying with a new PSP key (or falling over to a second PSP) can accidentally create a second charge.

## Alternatives

### A. Blind retry on timeout

**Rejected.** HTTP timeout is not a definitive no-effect signal. RabbitMQ redelivery, worker lease expiry, or PostgreSQL uniqueness cannot turn this into a safe financial operation.

### B. Stable provider idempotency + authoritative status inquiry

**Conditional preference.** Reuse a stable PSP operation/idempotency key if the PSP contract documents the exact scope, lifetime and behavior; correlate an inquiry by provider reference and merchant account; apply a verified definitive result. Persist \`Unknown\` while ambiguous.

### C. No automated retry when PSP lacks safe inquiry/idempotency

**Required fallback.** Persist \`Unknown\`, retain attempted-provider and request evidence, reconcile bank/PSP statements, escalate to Finance/PSP. A manual correction must be controlled, authorized and auditable.

## Hard invariants

- Unknown is not Failed.
- Generated QR / accepted PSP API request is not proof that customer money was received.
- No cross-provider retry after ambiguous PSP A response until independent no-effect proof.
- Authenticated, correlated PSP outcomes control financial state transitions.
- Replayed messages/callbacks cannot cause additional charges or stale state regression.
- Completion time and refund/late-payment semantics require provider-specific evidence.

## Contract questions

- Does PSP guarantee idempotent CreatePayment? For which merchant account, key, method and time horizon?
- Does PSP provide status inquiry by stable reference? Is it authoritative?
- What are PSP webhook signature, replay, ordering and callback retry guarantees?
- What if a VietQR transfer is received after QR expiry?
- Can two different PSP operations be linked to one logical payment without duplicate collection?

## Mandatory proof

PC-T04, PC-T05, PC-T06, PC-T07, PC-T08, PC-T09, PC-T11, PC-T14, PC-T19 in actual provider sandbox/fault simulator; Finance/PSP approval of ambiguous outcomes.

## Decision

**Open until real PSP integration contract and test evidence.** Do not promote this proposal to Accepted as a substitute for banking/PSP contracts.
