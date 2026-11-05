# 10 — Architecture Fitness Functions, Tests, Governance & Traceability

## Architecture fitness functions

Automated architecture conformance catches **specific classes of violations**. Passing checks is not proof that a payment architecture is safe.

| Rule | Check | When |
|---|---|---|
| Domain must not depend on Infrastructure | ArchUnitNET/NetArchTest-style dependency tests | PR CI |
| Module must not directly mutate another context's private repository | Architecture dependency tests and review | PR CI |
| API/events have compatible contract changes | OpenAPI/schema compatibility tests | PR CI |
| No plaintext credentials, secrets, tokens | Secret/SCA scanners | PR CI |
| Idempotency under concurrent requests | PostgreSQL integration/fault test | Build/release |
| Safe PSP unknown outcome and message redelivery | PSP sandbox / crash injection | Release |
| Merchant isolation | Authorization integration and negative tests | Build/release |
| p95/p99 and error rate under agreed workload | Load test and OTel metrics | Release |
| RTO/RPO and critical recovery | Game day / backup restore | Go-live |
| ADR and risk traceability | Review template and gates | PR review |

## Traceability example

BR-005 → FR-001 → NFR-001 / DRV-001 → R-001 duplicate charge → ADR-003 PostgreSQL atomic idempotency + ADR-006 safe PSP inquiry → INV-001/002 → T-COR-01/02 → GATE-COR.

All IDs are candidate artifacts until requirement owner acceptance.

## Test pyramid appropriate for financial correctness

1. Domain unit tests: state transition and money invariants.
2. Database integration: idempotency unique constraint, refund concurrency, outbox/inbox atomicity.
3. API/contract: merchant authorization, versioning, payload validation.
4. PSP sandbox: timeouts, definitive failure vs unknown, status inquiry, webhook authenticity.
5. Fault injection: worker crash, DB failover, MQ redelivery, cache outage.
6. Load/performance: p95/p99, query plans, pool saturation, queue lag.
7. Recovery/ops: backup restore, manual investigation and signed release checklist.

## Evidence ledger

Every test record needs:
- Test ID and Requirement/Risk/ADR links.
- Environment, date, code SHA, provider sandbox and workload version.
- Expected result, observed result, logs/traces and provider correlation.
- Pass/fail, owner, approval, mitigation for fail.

## PR policy

Feature branch → Draft PR into develop → user/architecture review → merge only after approval. Promote develop → main as a separately approved PR. Never auto-merge, force-push, or create a production release merely because a draft passes tests.

## Release gates

GATE-D1 discovery → GATE-DDD domain/ownership → GATE-ARC options/ADR → GATE-COR financial correctness → GATE-DATA DB/messaging → GATE-SEC security → GATE-PERF measured capacity → GATE-VAL architecture tests → GATE-GO readiness.
