# 00 — Customer Charter, Assumptions and Boundaries

## Customer request

A business requests a Payment Gateway integrating **VietQR, e-wallets and multiple PSPs**.

This is an initial customer statement, NOT a complete requirement or proof the platform is legally allowed to hold funds.

## Business outcomes to elicit

1. Unified merchant integration and stable merchant-facing API.
2. Offer multiple payment channels where merchant and bank/PSP contracts allow.
3. Reconciled, traceable state for each payment/refund and PSP attempt.
4. Reliable refund, callback and investigation lifecycle.
5. MVP within six months (working workshop target, not a contractual commitment).

## Confirm vs assume vs decide

| Item | Category | Current status | Who can confirm |
|---|---|---|---|
| Payment Gateway integrates VietQR/e-wallets/PSPs | Customer statement | Given | Sponsor/Product |
| Gateway does not directly hold or settle funds | Assumption | **Not confirmed** | Sponsor + Finance + Legal |
| Two PSP partners at MVP | Working assumption | **Not confirmed** | Product / PSP Lead |
| 10k/day normal volume and 30k/50k peak examples | Different workshop scenarios | **Not confirmed** | Product / SRE |
| 500 concurrent users | Workshop input | Needs behavior/arrival clarification | Product |
| ASP.NET Core 8, PostgreSQL, RabbitMQ and Redis | Engineering preference | To be evaluated | Architecture Board |
| Modular Monolith + workers | Proposed architecture choice | Not approved | Architecture Board |
| Money custody, PCI, anti-fraud and licensing | Regulatory / business boundary | **TBD** | Legal + Security |
| Budget | Constraint | **TBD** | Sponsor |
| Recovery (RTO/RPO), availability, p95 latency | NFR | **TBD** | Product + SRE |

## Critical stakeholder interview questions

**Business / Finance / Legal**
- Which legal entity receives, holds, transfers or settles funds? How does money move from payer to merchant?
- Who confirms final payment? Who signs off failed/unknown financial outcomes?
- Who authorizes refunds, chargebacks, disputes, merchant payouts and accounting?
- Is this orchestration only, or a funds-management product requiring a ledger?

**PSP / VietQR partner**
- Which specific bank/partner returns QR payment status, exact reference, amount and settlement report?
- What are the provider rules for payment creation idempotency, expiry, late transfers, partial refund and status inquiry?
- How is each inbound webhook authenticated and how are duplicates/replays treated?
- Is cross-provider failover ever safe after a timeout?

**Product**
- What does merchant see while waiting: QR, redirect, pending or asynchronous completion?
- What is the mandatory MVP and explicitly out of scope?
- Which business meaning is used for 10,000/day: actual payments or HTTP requests?

**Engineering / Security / Operations**
- What are peak windows, burst factor and retry amplification?
- What p95/p99, availability, RTO/RPO and retention targets are contractually accepted?
- What are tenant isolation, secrets, audit and data residency requirements?

## Proposed working boundary: Payment Orchestration

**In scope candidate:** merchant access, payment request, attempts, routing, provider adapters, callback verification, payment state, refund orchestration, reconciliation, merchant notifications and auditable operations.

**Out of scope candidate (until business requests it):** custody or financial ledger of merchant balances, independent payouts, payment-network settlement, raw card storage.

This boundary **must be signed off by Sponsor + Finance + Legal**. If it changes, redesign domain/data/security topology before implementation.

## Delivery artifacts and ownership

- Customer Brief: Sponsor / Product Owner.
- BR/FR/NFR + acceptance scenarios: Product / QA.
- Assumption/Decision Board and Risk Matrix: Architect + named owners.
- Financial operations RACI and money-flow diagram: Finance + Legal.
- PSP capability matrix: Integration Lead and PSP contracts.
- Architecture Proposal and ADRs: Architect / Review Board.
