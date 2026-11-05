# Architecture Risk Register

| ID | Risk | Likelihood | Impact | Mitigation | Verification | Owner |
|---|---|---|---|---|---|---|
| R-001 | Duplicate provider charge due to retry | TBD | Critical | Provider idempotency or explicit inquiry / recovery protocol | PSP failure test | TBD |
| R-002 | Unknown payment outcome after network timeout | TBD | Critical | Unknown state + status inquiry + reconciliation | Fault-injection test | TBD |
| R-003 | DB commit succeeds but event publishing fails | TBD | High | Transactional outbox | Crash test | TBD |
| R-004 | Webhook replay, forgery or out-of-order delivery | TBD | High | Signature verification, inbox/dedup, state rules | Security/integration test | TBD |
| R-005 | Wrong workload sizing | TBD | High | Confirm arrival distribution, load tests, re-estimate | Capacity test | TBD |
