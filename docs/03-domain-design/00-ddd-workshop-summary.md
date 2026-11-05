# DDD Workshop — From Customer Request to Boundaries

## Step 0 — Understand the business

Customer statement: "Build a Payment Gateway connecting VietQR, e-wallets and multiple PSPs."

Confirmed: integration channels are requested. NOT confirmed: who legally holds or settles money, which PSP/bank verifies VietQR payments, what refund policy applies or how payment success is defined.

**First hypothesis:** build a multi-merchant **non-custodial payment orchestration gateway**. If Finance/Legal rejects this boundary, the domain model may require Ledger, Settlement, Payout, Dispute and financial-controls contexts and must be redesigned.

## Step 1 — Discover business capabilities

Merchant management; payment orchestration; routing; provider adapter/integration; refund; reconciliation; merchant notifications; financial operations/audit investigation.

Capabilities describe what the business needs, not how many service processes to deploy. Map each capability to a core, supporting or generic subdomain. Where differentiating advantage is unknown, leave classification **Needs Confirmation**.

## Step 2 — Identify distinct vocabularies

- Merchant's "paid" may mean checkout completed, not funds settled at bank.
- Payment's "Succeeded" must require appropriate PSP evidence, not a generated QR.
- Provider's "success" may mean API accepted or instruction created; adapter maps it explicitly.
- Refund "approved" vs "submitted" vs "settled" are different.
- Reconciliation's "matched" means evidence aligns, not a command to overwrite Payment state.
- Unknown is an **epistemic state**: we do not know what happened at PSP.

## Step 3 — Draw boundaries

Boundaries are drawn around business rules, data ownership, and consistency needs, not around CRUD tables. See context catalog, context map, aggregates and event storming.

For MVP propose eight logical contexts, but revisit:
1. Routing could start as a dedicated policy module within Payment if no distinct domain language/team/scale exists.
2. Refund might remain under the Payment deployment/transaction boundary until atomic refund budget is proven.
3. Audit infrastructure is cross-cutting; only investigation/exception case management is potential separate Bounded Context.
4. Merchant Identity/Auth can be a separate generic identity mechanism while Merchant remains a business context.

## Step 4 — Defend correctness against failure

Scenario: Merchant posts once; PSP charges; worker crashes before persisting response; queue redelivers. Both the Payment model and PSP integration contract must prevent unsafe duplicate financial side effects. DB uniqueness alone does not prove PSP exactly-once behavior.

Scenario: Two concurrent refunds consume the same remaining refundable amount. The authorized money limit must be verified and reserved atomically; a stale cross-context projection must never over-authorize.

## Step 5 — Choose contracts and governance

- Each context exposes a stable public API/event/policy contract.
- No context directly writes another context's private table.
- In the same database, a shared transaction and shared outbox infrastructure **can** be used but ownership of business state remains clear. Mark any intentional exception explicitly.
- Integration events are at least once; consumer dedup, event versioning, ordering and replay policy are required.
- Every invariant maps to failure tests and architecture review gates.

## Exit criteria

Customer signs money-flow responsibility; PSP capability matrix completed; business agrees Payment/Refund success terms; context map and data ownership reviewed; 14 DDD gates have evidence/owner, and critical decisions are approved.

Do not call this design "implemented", "tested" or "approved" before those steps.
