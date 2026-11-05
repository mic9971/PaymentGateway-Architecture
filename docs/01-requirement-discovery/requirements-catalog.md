# Requirements Catalog (proposed, not yet approved)

| ID | Category | Requirement | Priority | Acceptance evidence | Status |
|---|---|---|---|---|---|
| FR-001 | Functional | Merchant creates payment | Must | API contract + integration test | Proposed |
| FR-002 | Functional | Merchant reads payment state | Must | API test | Proposed |
| FR-003 | Functional | Refund workflow | Must | Finance invariant test | Proposed |
| FR-004 | Functional | PSP webhook intake | Must | Signature/dedup tests | Proposed |
| FR-005 | Functional | Reconciliation | Must | Mismatch recovery test | Proposed |
| NFR-001 | Correctness | No unintended duplicate provider charge on request retry | Must | Fault injection + provider contract test | Proposed |
| NFR-002 | Security | Merchant tenant isolation | Must | Access control test | Proposed |
| NFR-003 | Performance | Define p95 API latency at agreed load | TBD | Load test | Open |
| NFR-004 | Recovery | Agree RTO/RPO and restore evidence | TBD | Recovery drill | Open |
