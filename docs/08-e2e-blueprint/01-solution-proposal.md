# 01 — Architecture Strategy, Assessment and Solution Proposal

**Solution concept:** a **multi-merchant Payment Orchestration Gateway** that integrates multiple PSPs, tracks outcomes reliably and can reconcile uncertain operations. This is a **proposed** business boundary until money custody is confirmed.

## Business drivers → quality attributes

| Driver | Scenario | Proposed measurable evidence (not approved SLO) |
|---|---|---|
| Financial correctness | Duplicate requests and ambiguous PSP timeout | No unintended duplicate financial effect in sandbox/fault tests |
| Recoverability | PSP succeeded but response or webhook lost | UNKNOWN persists; status inquiry / reconciliation finds evidence |
| Security | Merchant A requests Merchant B payment | Denied by authorization and row-scoped queries |
| Integrity | DB commit works but RabbitMQ publish fails | Transactional outbox eventually publishes |
| Performance | Peak window / retry storm | p95/p99 and error budget approved after workload discovery |
| Maintainability | Add a new PSP adapter | Provider semantics contained behind explicit contract |
| Operations | Operator investigates discrepancy | Payment/attempt/provider correlation and audit are available |
| Cost | Infrastructure budget and growth | Quotes, utilization and total operating costs measured |

## Options compared

### A. Modular Monolith + background workers (initial preference)
- Explicit bounded-context modules within one logical Payment Application; process-separated workers as required.
- Easier to share PostgreSQL ACID transaction boundary and avoid premature distributed coordination.
- Lower operational overhead, but independent deployment/scaling of each domain limited.
- Must enforce module dependency boundaries and database write ownership with architecture fitness tests.

### B. Multiple independently deployed Microservices
- Independent releases and fine-grained capacity per domain.
- Cost: more network failure modes, eventual consistency, ops/observability, orchestration and versioning.
- Consider only when team topology, isolation or measured scaling justifies it.

### C. Single monolithic service without module ownership
- Fastest very early prototype; greater coupling and long-term change risk.
- Avoid for this multi-domain payment application even if physically deployed as one process.

## Technology candidates, not irrevocable constraints

| Component | Proposal | Why | Trade-off / validation |
|---|---|---|---|
| HTTP API | ASP.NET Core | Mature .NET ecosystem | Version compatibility; supported runtime for go-live |
| PostgreSQL | Payment operational source of truth | Transactions, uniqueness, SQL | Availability, locks, IO, backup, schema boundaries |
| RabbitMQ | Asynchronous delivery | Decouple delivery and workers | At-least-once semantics, duplicates, DLQ and ops |
| Transactional Outbox | DB-state + message intent atomically | Avoid lost publish gap | Publisher lag + dedup needed |
| Redis | Non-authoritative cache/rate limit | Cache hot metadata | Staleness and outages; DB fallback |
| OpenTelemetry | Correlation and evidence | Trace API/worker/PSP spans | Sensitive data redaction + storage costs |

**Runtime lifecycle note:** Microsoft shows .NET 8 end-of-support in November 2026; evaluate a supported .NET LTS version at production go-live. Source: https://learn.microsoft.com/en-us/lifecycle/products/microsoft-net-and-net-core

## Architecture risks requiring a decision first

1. Unclear funds custody / regulatory responsibility.
2. Unconfirmed PSP idempotency and status inquiry — duplicate-charge risk.
3. Unknown VietQR confirmation authority and late bank transfer semantics.
4. No arrival-rate workload, NFR targets, budget or RTO/RPO.
5. Provider-side result races, webhook ordering, refund concurrency and reconciliation.
6. Operational burden of RabbitMQ, Redis and multi-region choices.

## Architecture Proposal recommendation (conditional)

Prefer **Option A** for a **non-custodial orchestration MVP** provided Business/Finance/Legal agree boundary, at least one PSP contract supports safe integration, and validated workload/availability can be met at affordable operating cost.

Never claim this is an approved architecture until ADR and release gates contain accountable sign-off and test evidence.
