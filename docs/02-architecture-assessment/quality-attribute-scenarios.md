# Quality Attribute Scenarios — Draft

| Attribute | Stimulus | Response | Measure | Status |
|---|---|---|---|---|
| Correctness | Client repeats same payment request | No unintended duplicate PSP charge | Concurrent/failure tests | Proposed |
| Recoverability | PSP call times out after charge | Unknown state, status inquiry and reconciliation | Failure drill | Proposed |
| Performance | Peak traffic at agreed load | API remains responsive | p95/p99 TBD | Open |
| Security | Merchant attempts cross-tenant access | Access denied and audited | Security tests | Proposed |
| Availability | Database/worker failure | Recovery per agreed targets | RTO/RPO TBD | Open |
