# 08 — Security, Trust Boundaries and Reliability Design

**Status:** Proposed, pending Product/PSP/Security/Finance/Legal approval.

## STRIDE-inspired threat scenarios

| Boundary | Main threat | Candidate mitigation | Proof |
|---|---|---|---|
| Merchant → Gateway | Stolen merchant API key | Credential rotation, least privilege, request authenticity, rate limit | Auth and key-rotation tests |
| Tenant boundary | IDOR: merchant reads another merchant's payment | Merchant ID in policy and DB predicate, deny by default | Cross-tenant negative tests |
| PSP → Webhook | Forged / replayed callback | Provider-specific signature, timestamp/replay checks, unique event inbox | PSP sandbox, forgery tests |
| Gateway → PSP | Secrets leakage or man-in-the-middle | TLS verification, secret manager, outbound policy | Security review |
| Worker → Database | Broad DB privileges | Schema ownership, least privilege, prepared SQL | Database role tests |
| Queue / Cache | Event replay or stale data | Event ID + consumer inbox, TTL/invalidation, DB source of truth | Replay and fallback tests |
| Log / Audit | Exposure of credentials/PII | Structured redaction, retention, access controls | Sample log inspection |
| Operations | Unauthorized financial status override | Maker/checker where required, audit, approved runbook | Privileged workflow test |
| Deploy pipeline | Secret, vulnerable package, unsigned artifact | CI secret scan, SCA, policy gating | Build evidence |
| Backup / recovery | Data theft or unrecoverable DB | Encryption, controlled restore, PITR/backup validation | Restore drill |

Actual PCI obligations, local financial regulations and provider webhook specs must be assessed with Legal/Security — a purely technical architecture diagram cannot determine compliance.

## Failure classes

- Client timeout: client doesn't know if Gateway accepted a payment → same Idempotency-Key retry.
- PSP timeout after side effect: Gateway does not know if PSP charged → UNKNOWN, inquiry, reconciliation; never blind failover.
- DB committed event but MQ down: Outbox waits and republishes.
- Consumer crash: message re-delivered; persistent dedup and safe attempt claim.
- PSP webhook late, duplicate or out of order: signed inbound event + conditional state machine.
- Merchant webhook down: outbound durable delivery log, finite retry and DLQ.
- Redis down: non-authoritative cache fallback; financial correctness unchanged.
- DB failover: reject unsafe writes/readiness; recover with validated RTO/RPO.
- Regional outage: topology decision pending cost and availability scenarios.

## Recovery targets (customer sign-off required)

| SLI / target | Proposed definition | Status |
|---|---|---|
| Availability | Good payment API responses / eligible requests per agreed window | Numeric target TBD |
| RTO | Restore service for agreed fault class | TBD |
| RPO | Max acceptable loss of committed gateway records | TBD |
| Unknown resolution | Time to verify provider outcomes / escalate discrepancy | TBD |
| Merchant webhook | Delivery attempt age and final outcome | TBD |
| Reconciliation | Statement matching window, unresolved discrepancy age | TBD |

## Game-day plan

1. Configure PSP sandbox to return success but drop response.
2. Kill payment worker after PSP invocation and before ack.
3. Delay/reorder/duplicate webhook events.
4. Stop RabbitMQ during DB commits, restart publisher.
5. Disable Redis while payment traffic is ongoing.
6. Force Postgres connection-pool saturation and failover in staging.
7. Restore database backup into isolated environment and verify payment/attempt/outbox counts against provider evidence.

Collect exact before/after timings, traces, counts and incident notes, not just "service recovered".
