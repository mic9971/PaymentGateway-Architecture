# DDD Review Gates & Decision Backlog (v1.1)

The workbook [DDD Decision Gate](https://docs.google.com/spreadsheets/d/1WfbSCEKQRXnYwJvuz7XT4Q_gZ5v0QPwt/edit?usp=drivesdk&ouid=116017105754217906634&rtpof=true&sd=true) contains 14 draft review gates. All require accountable owners and evidence; **none are customer-approved**.

| Priority | Gate | Question | Proposed owner | Required evidence |
|---|---|---|---|---|
| Critical | G-DDD-02 | Who legally holds/transfers/settles money? | Sponsor/Finance/Legal | Signed money flow and legal boundary |
| Critical | G-DDD-04 | Which PSP/VietQR confirmation/inquiry/idempotency rules apply? | PSP Lead | Actual partner contract & sandbox |
| Critical | G-DDD-05 | Which BC atomically controls refundable limit? | Finance/Architect | Concurrency ADR + fault tests |
| Critical | G-DDD-06 | When is safe PSP re-routing permitted? | Product/PSP/Architect | PSP failure classification + ADR |
| Critical | G-DDD-07 | Who owns PSP statement reconciliation and finality? | Finance | Matching rules + discrepancy RACI |
| Critical | G-DDD-14 | Are all financial invariants linked to tests? | QA/Finance/Product | Executable failure test plan |
| High | G-DDD-01 | Which capabilities actually differentiate this product? | Product Owner | Subdomain classification |
| High | G-DDD-03 | What are Paid, Accepted, Pending, Unknown and Settled? | Product/Finance | Ubiquitous Language |
| High | G-DDD-10 | Can each table have one business write owner? | Architect/DBA | Schema contract and permissions |
| High | G-DDD-11 | Are context map contracts explicit and versioned? | Architect/Tech Lead | Contract catalog and tests |
| High | G-DDD-12 | Are forbidden dependencies enforceable in CI? | Tech Lead | Architecture fitness rules |
| High | G-DDD-13 | Do module boundaries fit team/delivery constraints? | Engineering Lead | Deployment alternative ADR |
| Medium | G-DDD-08 | How are merchant webhooks signed, retried and replayed? | Product/Merchant | Outbound webhook contract |
| Medium | G-DDD-09 | Is Audit plumbing or a business Operations context? | Ops/Security | Clarified domain and ownership |

## Acceptance policy

- **Approved** only with verifiable stakeholder answer, owner and date; design preference is not approval.
- A risk can be accepted only by an authorized owner; architecture review alone does not absolve financial/compliance accountability.
- Merge to \`develop\` after explicit user review; \`main\` remains untouched until a separately approved merge.
- No production financial PSP integration until *custody scope, PSP semantics, refund invariant and test strategy* are signed off.

## Next actual design work

**Architecture Decision Workshop 1: Payment vs Refund reservation boundary.** Compare option A Refund-owned budget with option B Payment-owned reservation; test under concurrent refunds, PSP timeout, cancellations, partial capture/refund and reconciliation corrections. Document risk, owner and adopted contract.

**Architecture Decision Workshop 2: Provider routing and timeout/unknown handling.** Never assume PSP A timed out means it did not charge. Validate PSP idempotency and status inquiry; decide deterministic routing/failover rules and evidence capture.

Only after these workshops implement detailed EF Core entities, indexes, migrations and worker workflows.
