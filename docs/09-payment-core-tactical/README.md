# Payment Core Tactical Design — v1.2

**Status:** Proposed / design for review. No production code, PSP sandbox tests, migrations, or business approvals have been performed.  
**Scope:** Payment bounded context, not the entire custody, settlement or reconciliation product.  
**Continuity:** Day 1 Customer Discovery → DDD v1.1 → Payment Core tactical design v1.2.

## Excel design register

[**PaymentGateway_PaymentCore_TacticalDesign_v1.2.xlsx** — 64 worksheets](https://docs.google.com/spreadsheets/d/16LyqBQmwThYFXQz9x9LkdENNXnKhRfsH/edit?usp=drivesdk&ouid=116017105754217906634&rtpof=true&sd=true) preserves all 51 earlier E2E + DDD sheets and adds 13 Payment Core worksheets. Excel is stored on Google Drive, original XLSX format. GitHub Markdown and CSV snapshots are review artifacts, **not automatic sync copies**.

## Read this before implementation

| Order | Document | Main decision |
|---|---|---|
| 1 | [Scope and decisions](./01-scope-and-acceptance.md) | What Payment owns and what remains unknown |
| 2 | [Aggregates and invariants](./02-domain-aggregates.md) | PaymentIntent vs PaymentAttempt vs provider evidence |
| 3 | [State machine](./03-payment-state-machine.md) | QR-ready ≠ paid; UNKNOWN ≠ FAILED |
| 4 | [HTTP/application contracts](./04-use-cases-api.md) | Merchant payment and idem contract |
| 5 | [PostgreSQL design](./05-postgresql-and-idempotency.md) | Atomic claim, constraints, state history and outbox |
| 6 | [PSP side-effect/recovery](./06-provider-recovery.md) | Cross-system failure and why blind retry is unsafe |
| 7 | [Messaging and transaction design](./07-messaging-transaction-boundaries.md) | Database, RabbitMQ and inbox/outbox responsibilities |
| 8 | [.NET module interfaces](./08-dotnet-contracts.md) | Candidate C# contracts and dependency direction |
| 9 | [Test and evidence plan](./09-tests-and-gates.md) | 20 failure tests; 16 review gates |
| 10 | [Architecture options/ADRs](./10-adrs-and-decisions.md) | Attempt aggregate and unknown-recovery decisions |

## Required product/architecture confirmations

1. Who holds/settles money, and who can confirm financial finality?
2. Which real bank/PSP issues VietQR instructions and authoritative payment evidence?
3. Which partner supports idempotent create, status inquiry, webhook signature/replay and safe refunds?
4. What is the merchant checkout contract? A response of QR created is NOT proof payment succeeded.
5. What are actual workloads, SLO, RTO/RPO, compliance/budget and runtime support requirements?
6. Who atomically guards the RefundBudget? ADR-0013 remains open.

## Guiding invariants

- One merchant + operation + Idempotency-Key maps to one logical Payment; different payload with same key conflicts.
- Replayed request, message or webhook may not create an unintended second charge.
- PostgreSQL uniqueness alone **cannot** guarantee exactly-once PSP effects.
- Unknown provider outcome persists until verified or escalated; never silently treated as Failed.
- Provider callback is untrusted until validated, correlated and mapped to business finality.
- Payment is the sole authority for its state changes; other contexts issue commands/evidence.
- Transactions never remain open across PSP network requests.
- At-least-once RabbitMQ delivery requires durable dedup and attempts that survive process crashes.

## Workflow

Changes are committed only to \`feature/day01-requirements-discovery\` and linked to **Draft PR #1** into \`develop\`. Do not merge into \`develop\` or \`main\` without explicit user approval.
