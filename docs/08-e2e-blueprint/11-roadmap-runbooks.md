# 11 — Delivery Roadmap, Cutover, Operating Model and Runbooks

**Working scenario:** 6 months. Not a plan commitment until team size, provider onboarding, regulatory review and budget are confirmed.

| Window | Primary deliverables | Critical dependency | Exit evidence |
|---|---|---|---|
| W01–W04 | Discovery, DDD, boundary, options, initial ADRs | Sponsor, Finance, Legal, PSP | Signed business and architecture scope |
| W05–W08 | Solution foundation, Payment Core, PostgreSQL, tenant auth, idempotency | Signed data/API model | Core integration/concurrency tests |
| W09–W12 | Two PSP sandboxes, webhook, outbox/inbox, RabbitMQ | Signed PSP contracts/credentials | Safe timeout/redelivery evidence |
| W13–W17 | Refund, reconciliation and merchant notification | Financial refund/matching rules | Refund/race and discrepancy tests |
| W18–W22 | Security, load/profile/DB tuning, HA/restore, ops runbooks | Target SLO and RTO/RPO | Performance, security, game days |
| W23–W26 | UAT, staged rollout, cutover rehearsal, go-live/hypercare | All critical gates and owner sign-off | Business/Finance/Ops acceptance |

## Operations ownership

- Product: approve user journey and go-live scope.
- Finance: settlement evidence, refunds, discrepancies and escalations.
- PSP Lead: partner incident/contract/inquiry, credentials and sandbox.
- SRE/Platform: PostgreSQL, MQ, Redis, certificates, alerting and recovery.
- Security: incident response, credential compromise and compliance evidence.
- Architect: ADR trade-offs, conformance, change risk.
- QA: functional/integration/security/performance and failure test coverage.

## First-response runbooks

### Unknown payment growing
1. Inspect PSP timeout/error categories, correlation IDs and provider reference.
2. Stop unsafe retries or automatic PSP switch.
3. Run PSP inquiry if contracted; if unavailable, hold UNKNOWN for reconciliation.
4. Contact Finance/PSP with evidence; notify merchants consistently.
5. Resolve only with verified proof, record audit and follow-up prevention.

### MQ backlog
1. Compare outbox age and queue ready/unacked/dead-letter.
2. Determine consumer crash vs high ingress vs slow provider vs poison event.
3. Apply bounded concurrency and controlled replay after idempotency checks.
4. Verify no extra PSP financial side effects; confirm queue drains.

### PostgreSQL saturation
1. Inspect CPU, locks, active connections, query plans, I/O and pool timeouts.
2. Identify blocking queries and long transactions before raising pool sizes.
3. Optimize query/index/batch after baseline capture.
4. Load test equivalent workload and check payment correctness.

### PSP webhook forgery or key compromise
1. Reject unverifiable events.
2. Rotate secrets according to provider protocol; preserve minimal evidence.
3. Confirm who may replay signed events and how to avoid stale state.
4. Security/PSP sign-off before reopening callback acceptance.

### Database backup / restore
1. Follow documented RTO/RPO and choose authorized recovery point.
2. Restore to controlled environment or perform approved failover.
3. Verify consistency across payments, attempts, outbox/inbox and provider evidence.
4. Reconcile uncertain transactions; do not silently rewrite payment status.

## Go-live must prove
Customer scope, risk acceptance, real PSP sandbox conformance, performance/saturation, tenant/security, restore, webhooks, reconciliation, on-call, runbooks, cutover and rollback. No gate is marked approved in this version.
