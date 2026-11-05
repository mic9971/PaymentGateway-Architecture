# ADR-0014 — PaymentAttempt Aggregate Boundary

- **Status:** Proposed / not accepted.
- **Date:** 2026-11-05
- **Decision owner:** Payment Tech Lead + Solution Architect.
- **Affected contexts:** Payment, Provider Integration, Routing, Reconciliation.

## Context

PaymentIntent is one merchant's logical payment operation. PaymentAttempt represents an invocation at a particular PSP. An attempt's worker may crash, retry, receive late callbacks, or remain Unknown even while the aggregate PaymentIntent must retain a coherent financial outcome.

The team must decide whether PaymentAttempt is a child entity inside PaymentIntent aggregate or an independently persisted aggregate root with a documented cross-record consistency protocol.

## Option A — PaymentAttempt child of PaymentIntent

**Pros:** One aggregate controls all transitions, direct invariant checks, simpler conceptual ownership.

**Cons:** Large aggregate, update contention and loading an unbounded attempt collection; worker lease/claim may be awkward; a single aggregate operation can become hot.

## Option B — Separate PaymentAttempt aggregate, Payment-owned application coordination

**Candidate preference, NOT accepted.**

PaymentAttempt has stable AttemptId, PSP key/reference, provider identity, claim version, lease, evidence/outcome. PaymentIntent owns financial state and valid transition rules. Application service uses a short local PostgreSQL transaction to atomically correlate verified outcome, update allowed PaymentIntent state and append evidence/outbox. A durable claim does not guarantee external PSP was never called after lease expiry.

**Pros:** Independent attempt lifecycle/worker concurrency, clear provider-operation recovery, bounded write scope.

**Cons:** More consistency bookkeeping; state and attempts must be coordinated explicitly and audited.

## Decision criteria

1. Number of PSP attempts per logical Payment and transaction frequency.
2. Need for independent worker claiming and status inquiry.
3. Ability to enforce \`no unsafe repeat after Unknown\` under worker crash.
4. PostgreSQL lock/write contention and query plans at measured peak load.
5. Simplicity of financial correctness tests and state provenance.

## Mandatory tests

PC-T01 create request races; PC-T03 concurrent attempt claim; PC-T04 PSP charge-then-timeout; PC-T07 early webhook; PC-T11 broker redelivery.

## Review outcome

**Open.** Do not claim separate aggregates have been approved. This is a tactical DDD decision, not a mandate to deploy PaymentAttempt as a separate service.
