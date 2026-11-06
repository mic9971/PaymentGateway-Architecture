# Payment Gateway — Architecture-first Learning Project

> Status: **Scaffold for review (v0.1)**. No application code, financial integration, or production configuration has been implemented.

## Purpose

A single case study that starts with a customer's business request and evolves through discovery, architecture assessment, DDD, solution options, proposal, solution design, validation, and implementation with .NET/PostgreSQL/RabbitMQ/Redis.

## Working assumptions — NOT approved customer requirements

- New payment gateway; target go-live in approximately 6 months.
- Workshop figures (to confirm): 10,000 transactions/requests per normal day; 30,000 or 50,000 on peak days; 500 concurrent users.
- Intended provider integrations: two PSPs (which PSPs, capabilities, contracts, and compliance scope are TBD).
- Preferred learning stack: ASP.NET Core 8, PostgreSQL, RabbitMQ, Redis. Production target runtime support must be reassessed before go-live.
- Budget is **TBD**; earlier exercises discussed different budget scenarios, which must not be combined silently.

## Architecture status

Modular Monolith + workers is only a **candidate proposal**, not an approved decision. Do not implement payment execution, retries, or external side effects before correctness contracts are defined.

## Learning progression

1. `docs/00-customer-brief`: what the customer asked for.
2. `docs/01-requirement-discovery`: interview questions and traceable requirements.
3. `docs/02-architecture-assessment`: assumptions, quality attributes, risks.
4. `docs/03-domain-design`: capabilities, bounded contexts, and ownership.
5. `docs/04-solution-proposal`: options, trade-offs, cost estimates, ADRs.
6. `docs/05-solution-design`: HLD/LLD, data design, APIs and integration.
7. `docs/06-architecture-validation`: correctness, security, capacity, reliability, performance.
8. `docs/07-delivery-and-evolution`: roadmap, governance, production readiness.

`src/`, `contracts/`, `tests/`, and `deploy/` are planned structural boundaries, currently empty placeholders.

## Review gates

- **Gate A:** approve customer brief, assumptions and NFR ownership.
- **Gate B:** approve domain boundaries and solution options.
- **Gate C:** approve architecture proposal and design risks.
- **Gate D:** approve test evidence and release readiness.

Avoid production secrets and customer payment data in this repository.

## Day 2 — DDD Design Pack v1.1 (in review)

- [Strategic DDD documentation](docs/03-domain-design/README.md) — 8 candidate Bounded Contexts, capability map, context contracts, aggregates and invariants, event storming, glossary, PostgreSQL ownership, .NET module structure, DDD review gates.
- [Editable Excel workbook — 51 sheets](https://docs.google.com/spreadsheets/d/1WfbSCEKQRXnYwJvuz7XT4Q_gZ5v0QPwt/edit?usp=drivesdk&ouid=116017105754217906634&rtpof=true&sd=true) — previous E2E 42 tabs preserved, 9 new detailed DDD tabs.
- [ADR-0013 Refund boundary](docs/04-solution-proposal/adrs/ADR-0013-refund-boundary-and-reservation.md) — two financial consistency options still **Proposed**, not accepted.
- Work remains on `feature/day01-requirements-discovery` and [Draft PR #1](https://github.com/mic9971/PaymentGateway-Architecture/pull/1). No changes have been merged into `develop` or `main`.

## Day 3 — Payment Core Tactical Design v1.2 (Draft)

- [Payment Core Tactical Design Pack](docs/09-payment-core-tactical/README.md) — PaymentIntent, PaymentAttempt, Idempotency, PostgreSQL, PSP timeout/UNKNOWN, Outbox/Inbox, RabbitMQ and validation.
- [Editable Excel workbook — 64 sheets](https://docs.google.com/spreadsheets/d/16LyqBQmwThYFXQz9x9LkdENNXnKhRfsH/edit?usp=drivesdk&ouid=116017105754217906634&rtpof=true&sd=true) — all 51 E2E + DDD tabs retained, 13 Payment Core design tabs added.
- **New open decisions:** Attempt aggregate boundary (ADR-0014), provider timeout/idempotency recovery (ADR-0015). RefundBudget ADR-0013 remains open.
- **Tests:** 20 designed failure scenarios, **none run**. **Gates:** 16 open; no customer/PSP/Finance approvals claimed.
- Changes remain on `feature/day01-requirements-discovery` / Draft PR #1. No merge into `develop` or `main`.

## Payment Core Implementation Foundation v0.1 (Draft PR #2)

- [Implementation guide and Docker instructions](docs/10-implementation/README.md)
- [Implementation status tracker](docs/10-implementation/IMPLEMENTATION-STATUS.csv)
- [.NET 10 projects](src/) and [unit/integration tests](tests/)
- [Proposed ADR-0016: Production runtime](docs/04-solution-proposal/adrs/ADR-0016-production-runtime.md)
- [Draft code PR #2](https://github.com/mic9971/PaymentGateway-Architecture/pull/2): `feature/payment-core-foundation` → `feature/day01-requirements-discovery`. Separate from [Draft architecture docs PR #1](https://github.com/mic9971/PaymentGateway-Architecture/pull/1) → `develop`.

**Development-only:** Mock PSP, demo merchant header, and PostgreSQL-only Outbox persistence; there is no real VietQR/PSP, no RabbitMQ publisher, no production authentication. Verified CI: 9/9 automated tests passed for foundation commit 1729f962; this does not establish real PSP financial correctness. Do not merge without explicit approval.

- [Implementation Tracker Excel v0.1 — 6 sheets](https://docs.google.com/spreadsheets/d/1GSFJbTdbNP5JpYwJWmZccv2A4vSEHo7R/edit?usp=drivesdk&ouid=116017105754217906634&rtpof=true&sd=true) — features, test evidence, risks, ADR gates, delivery plan and local runbook.
- [GitHub Actions evidence: 3 unit + 6 PostgreSQL integration tests PASSED](https://github.com/mic9971/PaymentGateway-Architecture/actions/runs/41969726629).

## Messaging Implementation — RabbitMQ Outbox + Inbox v0.2

- [Implementation design, failure contracts and local runbook](docs/11-messaging-implementation/README.md).
- [Architecture decision ADR-0017](docs/04-solution-proposal/adrs/ADR-0017-outbox-rabbitmq-inbox.md).
- [Messaging Implementation Tracker v0.2 — 11 Excel tabs](https://docs.google.com/spreadsheets/d/1_hs9dEStZ2208wy6APN1e2YX4B4qX0hG/edit?usp=drivesdk&ouid=116017105754217906634&rtpof=true&sd=true) (prior 7 tabs preserved plus 4 messaging review tabs).
- [GitHub Actions evidence: 3 unit + 9 PostgreSQL/RabbitMQ integration tests passed](https://github.com/mic9971/PaymentGateway-Architecture/actions/runs/41982031767).
- Feature branch: `feature/rabbitmq-outbox-inbox`, based on `feature/payment-core-foundation`. **Do not merge without review.**
- This increment publishes **payment integration events** only. RabbitMQ messages do **not** trigger Mock PSP calls or any bank charges.

## Payment Recovery & Fault Injection v0.3 (Draft PR #4)

- [Payment Recovery Guide, failure matrix and local game days](docs/12-payment-recovery/README.md).
- [ADR-0018: resolve Unknown through read-only inquiry](docs/04-solution-proposal/adrs/ADR-0018-safe-unknown-recovery.md).
- [Implementation Tracker v0.3 — 15-sheet Excel](https://docs.google.com/spreadsheets/d/1OzzZGORlTJ0tEIjk9MTlvAmXjLp2KZDa/edit?usp=drivesdk&ouid=116017105754217906634&rtpof=true&sd=true); 11 earlier sheets retained, 4 recovery review sheets added.
- [Verified CI: 3 unit + 16 PostgreSQL/RabbitMQ integration tests passed](https://github.com/mic9971/PaymentGateway-Architecture/actions/runs/42047986687).
- This is a **local-only Mock PSP experiment**; no bank account, VietQR, e-wallet or real PSP is called. No real-money exactly-once proof.
- Branch `feature/payment-recovery-fault-injection` should only enter the existing PR stack after explicit owner approval; `develop` and `main` remain untouched.

## PSP Webhook Security & Reconciliation v0.4 (Draft PR #5)

- [Webhook/reconciliation implementation guide and context boundaries](docs/13-psp-webhook-reconciliation/README.md).
- [Proposed ADR-0019](docs/04-solution-proposal/adrs/ADR-0019-verified-webhook-reconciliation.md).
- [Implementation Tracker v0.4 (21-sheet Excel)](https://docs.google.com/spreadsheets/d/1-KWKpiXq7nJyy0ItsacSh-tJRvRXy8OE/edit?usp=drivesdk&ouid=116017105754217906634&rtpof=true&sd=true) — prior 15 sheets preserved + six new status, security, reconciliation, tests and gate tabs.
- [Verified GitHub Actions CI: 25/25 tests](https://github.com/mic9971/PaymentGateway-Architecture/actions/runs/42054657420) — 3 unit + 22 PostgreSQL/RabbitMQ integration tests.
- **Development-only Mock PSP HMAC**; no real provider key, production merchant authentication, bank statements, or funds movement.
- Branch `feature/psp-webhook-reconciliation` is based on recovery Draft PR #4. No merge to any parent branch, `develop` or `main` without explicit approval.

## Refund Domain & Concurrent Refund v0.5 (Draft PR #6)

- [Refund implementation, model, invariant and local runbook](docs/14-refund-implementation/README.md).
- [Proposed ADR-0020 — RefundBudget experiment](docs/04-solution-proposal/adrs/ADR-0020-refund-budget-mock.md), refining still-open [ADR-0013](docs/04-solution-proposal/adrs/ADR-0013-refund-boundary-and-reservation.md).
- [Implementation Tracker v0.5 — 27 Excel sheets](https://docs.google.com/spreadsheets/d/1-bvaLzSISmrZNTg27gQIVRS4us8xEYxo/edit?usp=drivesdk&ouid=116017105754217906634&rtpof=true&sd=true), previous 21 tabs retained + 6 new Refund tabs.
- [Verified CI 32/32 — 3 unit + 29 PostgreSQL/RabbitMQ integration tests](https://github.com/mic9971/PaymentGateway-Architecture/actions/runs/42153098364).
- Only `MOCK` Refunds; RefundBudget is locally transactional with concurrent guard. No bank refund and no real financial/settlement entitlement proof.
- `feature/refund-budget-concurrency` → `feature/psp-webhook-reconciliation` (Draft PR #5). **No merges** without explicit approval.
