# Day 1 Customer Discovery — Review Meeting Script (45 minutes)
**Purpose:** Determine what problem the Payment Gateway solves and close high-risk unknowns before any Architecture Proposal sign-off.

| Minute | Topic | Facilitator question | Desired artifact |
|---|---|---|---|
| 00–05 | Goals and user journeys | Who buys the platform and what business outcome matters? | BR baseline |
| 05–15 | Funds custody and settlement | Who holds and moves money; who confirms success, refunds, settlement and chargebacks? | Signed money-flow/RACI |
| 15–25 | VietQR, e-wallet, PSP semantics | Which partner APIs, callback signatures, status lookup, idempotency and delayed payment rules? | PSP capability evidence |
| 25–33 | Functional MVP and exception scenarios | What merchant sees at timeout, pending, unknown, refund and mismatch? | Payment Journey and Exception Catalog |
| 33–40 | Quality, workload and cost | Traffic definition, peak window, p95, availability, RTO/RPO, compliance, budget, 6-month scope? | NFR and constraint backlog |
| 40–45 | Decisions and next actions | Which items can be approved today; what evidence and owners are still missing? | Decision board and gates |

## Participant roles
- Sponsor: business model and budget, contractual sign-off
- Product Owner: merchant journeys and release scope
- Finance/Operations: money-flow responsibility, refund/settlement/reconciliation
- Legal/Compliance/Security: custody, data boundary, regulatory and threat scope
- PSP Integration Lead: signed provider API behavior, sandbox and SLA
- Architect + Engineering + SRE: technical options, capacity, recovery evidence
- QA: measurable acceptance/failure scenarios

## Meeting output template
- Confirmed facts, with source and date:
- Assumptions created/changed, with owner:
- Requirement change IDs:
- Evidence requested from PSP/Finance/Legal:
- Open architecture decisions:
- Next review date:
- Customer approval reference:

**Rule:** Silence, optimistic defaults and vendor marketing descriptions do not count as approval.
