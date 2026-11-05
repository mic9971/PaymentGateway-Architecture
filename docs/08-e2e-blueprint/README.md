# Payment Gateway — End-to-End Architecture Blueprint (v1.0)

**Status:** Proposed / Working Draft, not an approved production design.  
**Date:** 2026-11-05  
**Repository workflow:** feature/day01-requirements-discovery → Draft PR #1 → develop → main (only when authorized).

## Canonical editable project workbook

[PaymentGateway_E2E_Architecture_Product_Pack_v1.0.xlsx](https://docs.google.com/spreadsheets/d/1d4ab3DIkGC_BYZUiJ34SSCEWjXENAC8U/edit?usp=drivesdk&ouid=116017105754217906634&rtpof=true&sd=true)

This Excel workbook has **42 tabs**: the existing 15 Day-1 Product Discovery tabs preserved, plus 27 new end-to-end design, capacity, security, validation, governance and operations tabs. It is stored in Drive in **Excel format**, not converted to native Google Sheets. It is the detailed structured register; repository Markdown files are reviewable architectural explanations.

## Reading order

| Step | Document | Why |
|---|---|---|
| 1 | [Customer Charter](./00-customer-charter.md) | What the customer asked, what's assumed |
| 2 | [Solution Options & Proposal](./01-solution-proposal.md) | Why propose the candidate architecture |
| 3 | [Domain Map](./02-domain-and-ownership.md) | Capabilities, bounded contexts, write ownership |
| 4 | [C4 / Logical Topology](./03-architecture-topology.md) | Components and trust boundaries |
| 5 | [Payment / Refund Sequences](./04-payment-and-refund-flows.md) | How money-related workflows behave |
| 6 | [PostgreSQL & Consistency](./05-postgresql-design.md) | Data model, state, idempotency, outbox/inbox |
| 7 | [MQ & Cache](./06-messaging-and-cache.md) | RabbitMQ reliability, Redis safety |
| 8 | [API & Event Contracts](./07-contracts.md) | Merchant and provider boundaries |
| 9 | [Security & Reliability](./08-security-reliability.md) | Threats, incidents, recovery |
| 10 | [Performance & Cost](./09-performance-cost.md) | Evidence-driven capacity and TCO |
| 11 | [Governance & Tests](./10-validation-governance.md) | Fitness functions and proof |
| 12 | [Roadmap & Operations](./11-roadmap-runbooks.md) | Delivery to go-live |
| 13 | [ADRs & Open Decisions](./12-adrs-and-gates.md) | Review/approval process |

## Architecture philosophy

Business problem → requirements → assumptions → quality attribute scenarios → risks → business capabilities & DDD → alternatives & trade-offs → architecture proposal → data and integration correctness → deployment/performance/security → test evidence → controlled delivery.

Do not mark any unconfirmed customer information as a fact. Notably:
- 10,000/day may be payments or requests; 30,000/50,000 peak days were separate workshop scenarios.
- 500 concurrent users is not 500 TPS.
- Budget is TBD; past exercises used differing budgets.
- PSP vendors/contracts, VietQR confirmation mechanisms, transaction authority, RTO/RPO, SLO and PCI scope remain TBD.
- .NET 8 is a requested learning stack; a supported runtime for production go-live must be chosen after lifecycle review.

## Gates

No Architecture Proposal sign-off until Money Custody, provider API semantics, real workload, security scope, measurable SLO and budget are owned and acknowledged. No go-live until payment correctness, restore, security and production failure-mode evidence are collected.

## Status summary

- Requirements: proposed, partially workshop-derived.
- C4 topology: **candidate**, not deployed.
- APIs/DDL/events: candidate contracts, not implemented.
- Financial correctness: test design only, not proven.
- Sizing and dollar cost: illustrative scenario or unquoted input, not production estimate.
- Release readiness: **not ready**.
