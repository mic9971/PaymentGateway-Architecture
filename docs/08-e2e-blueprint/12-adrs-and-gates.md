# 12 — ADR Register, Open Decisions and Architecture Review Gates

## Proposed ADRs (NOT accepted)

| ADR | Decision | Position | Needs validation |
|---|---|---|---|
| ADR-001 | Modular Monolith + background workers vs Microservices | Candidate: Modular Monolith | Team topology, deploy isolation, workload |
| ADR-002 | PSP adapters and versioned contracts | Candidate | Sandbox semantics for two real partners |
| ADR-003 | PostgreSQL for payment operational truth | Candidate | Transaction isolation, RPO/RTO, scale |
| ADR-004 | Transactional Outbox + RabbitMQ | Candidate | Guaranteed publish after DB commit, duplicates |
| ADR-005 | Deterministic routing rules | Candidate | Merchant restrictions and PSP costs |
| ADR-006 | UNKNOWN + inquiry instead of blind retry | Candidate | PSP idempotency/status lookup |
| ADR-007 | Redis only for non-authoritative cache | Candidate | Measured read/latency benefit |
| ADR-008 | Single-region high availability first | Candidate | Region, sovereignty, RTO/RPO |
| ADR-009 | Supported .NET LTS at go-live | Candidate | Runtime support lifecycle, upgrade plan |
| ADR-010 | No partitioning/sharding without growth evidence | Candidate | Volume, retention and plan data |
| ADR-011 | Separate Payment and Refund state machines | Candidate | Refund policy and race safeguards |
| ADR-012 | Tenant isolation + audited financial operations | Candidate | Compliance/security review |

## Highest-risk open questions

1. Who legally holds and settles customer money? (Sponsor, Finance, Legal)
2. Which bank/PSP owns VietQR payment confirmation and late-transfer semantics? (PSP Lead)
3. What does each PSP guarantee for idempotent create, inquiry, refund and signed webhook? (PSP Lead)
4. Is checkout allowed to return Pending rather than definitive outcome? (Product)
5. Are 10k/day payment transactions or API requests? Peak histogram? (Product/SRE)
6. What budget, p95/p99, availability, RTO/RPO, retention, data residency and compliance? (Sponsor/SRE/Security)
7. What staffing and critical external dependencies fit the six-month plan? (Sponsor/PM)

## Approval criteria

**Discovery Gate:** Signed business scope, money flow, success definition, PSP list, MVP and NFR owners.

**Architecture Gate:** Domain map, trade-offs, risk mitigations, ADRs and architecture diagram approved.

**Design Gate:** Invariants, Data/PSP/Event contracts, security threat model and recovery strategy reviewed.

**Validation Gate:** Sandbox proof, concurrency/fault tests, load tests and restore drills executed with evidence.

**Go-live Gate:** Finance/Security/Ops/Business acceptance, incident playbooks, rollout and rollback practice.

The operational principle: status Proposed or In Review is **not Approved**. PR merge alone cannot substitute for business and financial sign-off.
