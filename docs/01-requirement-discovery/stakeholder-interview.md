# Stakeholder Interview Guide

## Sponsor / Business owner
- What is the business problem and first-release scope?
- Who owns the budget, approval and go-live decision?
- Are we facilitating payments or holding/transferring funds?

## Product owner
- What payment methods, user journeys, currencies, refund rules, and merchant requirements are in scope?
- Does checkout allow asynchronous outcomes or must it wait for PSP?
- Are 10,000/day HTTP requests or payment transactions? Peak time distribution?

## PSP / Security / Operations
- Which PSPs and idempotency, query-by-reference and webhook semantics do they support?
- What PCI, fraud, privacy, RTO/RPO, residency and audit requirements apply?
- What are the target SLOs, incident response and reconciliation responsibilities?

Record each answer as Confirmed / Assumption / TBD with owner and date.
