# Strategic DDD — Design Pack v1.1

**Status:** Draft, pending Business/Finance/Legal, PSP and Architecture Review Board approval.  
**Scope:** Payment Orchestration Gateway integrating VietQR partner, e-wallets and other PSPs. Money custody is **not confirmed**.

[Editable Excel DDD v1.1 — 51 sheets](https://docs.google.com/spreadsheets/d/1WfbSCEKQRXnYwJvuz7XT4Q_gZ5v0QPwt/edit?usp=drivesdk&ouid=116017105754217906634&rtpof=true&sd=true)

## Read in order

1. [DDD Workshop Summary](./00-ddd-workshop-summary.md) — scope and how to read the model.
2. [Bounded Context Catalog](./01-bounded-context-catalog.md) — eight logical contexts, with proposed ownership.
3. [Context Map & Contracts](./02-context-map-and-contracts.md) — synchronous and asynchronous relationships.
4. [Aggregates & Invariants](./03-aggregates-and-invariants.md) — transactional correctness and hardest trade-offs.
5. [Event Storming](./04-event-storming.md) — commands, events, policies and alternate/failure flows.
6. [Ubiquitous Language](./05-ubiquitous-language.md) — provider/business language translation.
7. [Data Ownership & Module Layout](./06-data-ownership-module-layout.md) — PostgreSQL schemas and .NET module boundaries.
8. [DDD Review Gates](./07-ddd-review-gates.md) — issues requiring real stakeholder decisions.

## 9 new workbook tabs

DDD Readme, DDD BC Catalog, DDD Capabilities, DDD Context Map, DDD Aggregates, DDD Language, DDD Context Contracts, DDD Event Storming, DDD Decision Gate. All 42 previous E2E workbook tabs remain.

## Architecture position

**Eight logical bounded contexts are candidates, not eight deployable microservices nor eight named teams.** Initial deployment candidate remains Modular Monolith + separate background workers.

- Payment owns the logical PaymentIntent, PaymentAttempt, status transitions and idempotency.
- PSP Integration is an **Anti-Corruption Layer** translating provider-specific status, webhook and errors into normalized contracts. It does not own Payment business state.
- Routing owns route policy and evidence; an unknown prior attempt cannot be moved blindly to another provider.
- Refund owns refund lifecycle; payment-versus-refund atomicity is an **open architecture decision**.
- Reconciliation owns external-vs-internal evidence analysis and discrepancies, never directly edits Payment schema.
- Notifications owns merchant webhook delivery, not payment truth.
- Audit capture is cross-cutting; Operations/Investigation may be a separate business context when domain behavior justifies it.
- RabbitMQ, Redis, EF Core, PostgreSQL and Outbox are **technologies/patterns**, not automatically bounded contexts.

## Review workflow

Review the Excel gate register, correct the conceptual boundaries, obtain provider contracts, decide transaction constraints, document accepted ADRs, then implement architecture tests. The branch remains under **Draft PR #1**; no merge to develop/main without user approval.

## Payment Core v1.2 — tactical follow-on

[Read the technical design](../09-payment-core-tactical/README.md) and [open the 64-sheet workbook](https://docs.google.com/spreadsheets/d/16LyqBQmwThYFXQz9x9LkdENNXnKhRfsH/edit?usp=drivesdk&ouid=116017105754217906634&rtpof=true&sd=true). This refines PaymentIntent, PaymentAttempt and IdempotencyRecord, but **does not approve** aggregate boundaries, PSP outcome policies or RefundBudget. See the proposed ADR-0014/0015 in Payment Core and the existing ADR-0013 in Solution Proposal. No migrations or actual PSP calls have been implemented.
