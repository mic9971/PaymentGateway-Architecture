# Day 1 — Business Boundary Review (v0.2)
**Status:** Working draft. **All proposed scope and responsibilities require named owner and evidence.**

## 1. Business request received
"Build a payment gateway supporting VietQR, e-wallets and multiple PSPs."

This sentence does **not** define whether the platform actually holds funds, performs merchant payouts, acts as a payments orchestrator, or is a regulated intermediary. Do not choose a data/ledger topology before this boundary is confirmed.

## 2. Options and consequence comparison

| Decision dimension | A: Orchestration Gateway (working hypothesis) | B: Platform with funds-management responsibility |
|---|---|---|
| Money flow | Payer moves funds through contracted bank/PSP flows | Platform may be contractually responsible for custodial/settlement funds |
| Gateway's core | Payment orchestration, attempts, routing, webhook, refund requests, reconciliation | Adds ledger/balances, payout lifecycle, settlement controls, more finance operations |
| Source of truth | PostgreSQL tracks internal payment instructions and known provider outcomes; PSP/bank authoritative for its financial effects | Internal ledger may be financial source of truth for balances, subject to financial reconciliation |
| Failure model | UNKNOWN outcome, status inquiry, provider idempotency, audit, reconciliation | All of A plus ledger posting, posting reversals, payouts and stronger financial controls |
| Major unknown | Who owns money movement and settlement? | What is the legal authorization, financial accounting and operational mandate? |
| Proposed direction | Prefer A **only if Sponsor + Finance + Legal confirm no custody** | Explore B **only if business needs it**; materially expands scope and risk |

## 3. Proposed boundary — **not customer approved**
**Inside gateway:** merchant authentication/authorization; create and track payment attempts; choose compatible PSP adapter; validate inbound signed notifications; maintain payment lifecycle and audit trail; orchestrate refund; compare payment status with provider reports; notify merchant.

**Outside gateway unless explicitly contracted:** physically holding customer funds; direct bank settlement and merchant payouts; payer banking authorization; assuming PSP response/HTTP status is settlement proof; freely retrying a timed-out unknown provider charge across PSPs.

**VietQR check:** Identify the bank/PSP partner; distinguish QR creation, actual payment execution, event/statement confirmation, expiry/late arrival and refunds. "VietQR" alone is not a complete integration contract.

## 4. E2E happy path and uncertain path
1. Merchant submits `POST /payments`, authenticated with idempotency key.
2. Gateway atomically records the logical payment and attempt (PostgreSQL candidate).
3. Routing picks an allowed provider after capability/merchant constraints.
4. PSP adapter creates payment using stable correlation and provider-side idempotency **if available**.
5. Merchant displays QR, redirects to wallet or polls status. Showing QR is **not proof of funds**.
6. Payer approves the payment through wallet/bank/PSP.
7. PSP returns a response, webhook or status result. Verify authenticity and correlation.
8. Gateway records valid transitions and makes result queryable.
9. Notification worker delivers merchant webhook with at-least-once semantics and a stable event ID.
10. A timeout or missing callback leaves `Unknown` until status inquiry, reconciliation or controlled investigation can establish truth.

The order above is a **logical illustration**; provider APIs differ, callbacks may arrive early, late, duplicate or out of order.

## 5. Non-negotiable financial correctness invariants — candidates
- A given merchant/operation/idempotency key cannot unintentionally produce two financial charges.
- `UNKNOWN` is not `FAILED`; no blind retry or cross-PSP failover in the uncertain window.
- A payment's state reflects verifiable evidence; delayed webhook cannot regress settled/verified state.
- Refund aggregate cannot exceed the independently verified refundable amount.
- Every externally relevant attempt is correlated with merchant, provider and audit records.
- Merchant A must never read or change Merchant B's payment data.

These are candidates for requirement approval and failure tests, not claims that the current implementation already meets them.

## 6. Highest-value clarification questions (ask owners, don't substitute guesses)
| Priority | Question | Owner | Why it changes architecture | Evidence |
|---|---|---|---|---|
| P0 | Does the platform ever custody, receive, transfer or settle funds? | Sponsor + Finance + Legal | Determines product boundary, compliance scope, need for financial ledger | Money-flow contract/RACI |
| P0 | Which entity confirms VietQR payment and which identifiers correlate invoice to transfer? | PSP Integration Lead | Correctness of QR state and reconciliation | Partner contract + sample records |
| P0 | Which wallets and PSPs are actually contracted? | Product + PSP Lead | Provider API semantics and delivery | Contracts and sandbox access |
| P0 | Is payment success immediate, pending customer action, or eventually confirmed? | Product Owner | Sync/async checkout, API response contract | Merchant journey and UX approval |
| P0 | What is safe PSP retry and status-query behavior after timeout? | PSP Integration Lead | Avoid duplicate charge, safe failover design | PSP API docs and fault tests |
| P0 | Who is responsible for refund, disputes, settlement and reconciliation? | Finance | Data ownership, escalation and financial risk | Finance operations RACI |
| P1 | What does 10k/day count and how are peaks distributed? | Product + SRE | RPS/TPS, worker capacity, pool sizing | Traces, estimates and traffic profile |
| P1 | What are budget, p95 SLO, availability, RTO/RPO, and retention? | Sponsor + SRE | Infra options, data resilience and cost | Signed measurable NFRs |
| P1 | Does gateway touch raw cardholder data? | Security + Legal | PCI/security boundary and data controls | Data flow + compliance assessment |

## 7. Decision dependencies
```text
Funds custody → legal and accounting scope → domain boundaries → data model → architecture options
PSP contracts  → retry/unknown outcome semantics → state machine → integration design
Traffic / SLO → workload scenarios → capacity and indexing → measured cost
Security scope → trust boundaries → access controls, secrets, logs, audit retention
```

Do not approve Router, RabbitMQ or Redis as hard requirements from the product brief alone; justify them with evidence. Prefer a simpler candidate initially, but record competing options and trade-offs.

## 8. Day 1 exit conditions
All 13 review gates in the [v0.2 workbook](https://docs.google.com/spreadsheets/d/1NG0C3T2jWS5pj3Kcuvf6vfQVbB2kKY6Z/edit?usp=drivesdk&ouid=116017105754217906634&rtpof=true&sd=true) must be actively evaluated; critical gates must be approved before final Architecture Proposal. An unapproved target does not become approved merely because an architect made a recommendation.

**Next step after gate review:** derive Quality Attribute Scenarios and a traceable Architecture Driver Matrix from confirmed stakeholder answers.
