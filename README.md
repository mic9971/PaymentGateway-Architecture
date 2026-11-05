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
