# 09 — Failure-Test Catalog, Evidence Ledger and Review Gates

## Design acceptance tests — NOT RUN

| ID | Failure scenario | Required proof |
|---|---|---|
| PC-T01 | Two same-key create requests at same instant | Exactly one logical Payment, same PaymentId |
| PC-T02 | Same key with different amount | Conflict, original unchanged |
| PC-T03 | Two workers claim one attempt | One durable owner; no unsafe provider operation |
| PC-T04 | PSP charged but response lost/worker killed | Unknown + inquiry; no blind re-charge |
| PC-T05 | Definitive PSP no-effect decline | Failed with evidence, not timeout inference |
| PC-T06 | Verified paid PSP result | Succeeded only after auth/correlation/finality |
| PC-T07 | Webhook arrives before HTTP response | No illegal state regression |
| PC-T08 | PSP inquiry finds paid after missing webhook | Unknown resolved with proof |
| PC-T09 | Forged/duplicate/out-of-order webhook | Forged rejected, duplicates guarded |
| PC-T10 | DB commit, then RabbitMQ unavailable | Outbox eventually delivers |
| PC-T11 | Consumer ack lost and event re-delivered | No second financial effect |
| PC-T12 | Merchant A accesses Payment B | Denied and audited |
| PC-T13 | Unapproved admin manually changes payment | Denied; controlled correction only |
| PC-T14 | VietQR transfer arrives after QR expiry | Finance/PSP policy, not false Failed |
| PC-T15 | Idempotency key reused after expiration | Documented and tested retention behavior |
| PC-T16 | Two concurrent refunds over-allocate balance | Atomic budget prevents over-refund |
| PC-T17 | PostgreSQL connection lost mid-create | No half-committed payment/outbox |
| PC-T18 | Redis outage during CreatePayment | Correct payment semantics preserved |
| PC-T19 | PSP returns HTTP 200 only meaning QR ready | No premature Succeeded |
| PC-T20 | Read during callback-induced state change | Tenant-safe coherent state |

## Evidence ledger for each run

~~~text
Test ID:
Requirement / Risk / Invariant / ADR:
Code commit SHA:
Environment / provider sandbox:
Workload / test input:
Fault-injection setup:
Expected outcome:
Observed outcome:
PaymentId / AttemptId / provider reference:
Traces, DB rows, MQ evidence:
Pass/Fail:
Tester:
Reviewed by:
Date:
Residual risk:
~~~

## Sixteen open review gates

| Gate | Review decision | Owner | Status |
|---|---|---|---|
| G-CORE-01 | Money custody/settlement | Sponsor / Finance / Legal | Needs Confirmation |
| G-CORE-02 | Paid vs settled/finality | Finance / Product / PSP | Needs Confirmation |
| G-CORE-03 | PSP idempotency + inquiry | PSP Lead | Needs Confirmation |
| G-CORE-04 | VietQR expiry and late transfer | PSP / Finance | Needs Confirmation |
| G-CORE-05 | Idempotency API contract/retention | Product / Architect | Needs Confirmation |
| G-CORE-06 | Payment state transitions | Finance / Product | Needs Confirmation |
| G-CORE-07 | Attempt aggregate boundary | Architect | Needs Confirmation |
| G-CORE-08 | Unknown financial recovery | PSP / Finance | Needs Confirmation |
| G-CORE-09 | DB uniqueness/CAS migration | DBA / QA | Not Run |
| G-CORE-10 | Outbox + RabbitMQ + inbox tests | Platform / QA | Not Run |
| G-CORE-11 | Webhook authenticity/order | Security / PSP | Not Run |
| G-CORE-12 | Refund budget owner (ADR-0013) | Finance / Architect | Needs Confirmation |
| G-CORE-13 | Merchant tenant isolation | Security / QA | Not Run |
| G-CORE-14 | Peak workload/SLO | Product / SRE | Needs Confirmation |
| G-CORE-15 | Supported production .NET runtime | Engineering | Needs Confirmation |
| G-CORE-16 | Evidence archive and acceptance | Architecture Board / QA | Not Run |

All are pending/not run. **A passed static architecture test would not prove PSP correctness, capacity or go-live readiness.**

## Critical traceability

~~~text
Business BR → Functional FR → Quality scenario NFR
              ↓
       Financial invariant + Risk
              ↓
       Architecture Decision (ADR)
              ↓
   DB/PSP/MQ mechanism and Contract
              ↓
     Fault-injection / Sandbox Test
              ↓
    Evidence → Reviewer → Approval Gate
~~~

## Next review workshop

1. Validate actual merchant payment / VietQR status semantics with Finance and PSP.
2. Decide \`PaymentAttempt\` aggregate boundary.
3. Decide provider idempotency/inquiry recovery policy.
4. Keep RefundBudget ADR-0013 open until atomic reservation can be proven.
5. Only then author migrations, repositories and tested .NET skeleton.
