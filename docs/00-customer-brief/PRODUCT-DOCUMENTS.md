# Product / Architecture Excel Register — v0.1

> **Status:** Draft; not approved by any customer, PSP, Finance, Security, or Architecture Review Board.
>
> **Document date:** 2026-11-05
>
> **Working branch:** `feature/day01-requirements-discovery`. Changes should be reviewed by PR into `develop`, then promoted to `main` when approved.

The three v0.1 Excel workbooks below are the **baseline references**. Product Discovery v0.2 is now the **active Day 1 discovery draft**. Preserve v0.1 for comparison; do not treat any workbook as an approved customer contract. They are stored as original `.xlsx` files on connected Google Drive (not converted to native Google Sheets). The v0.1 baselines contain 26 worksheet tabs; Product Discovery v0.2 retains its original 9 tabs and adds 6 deep-review tabs.

| Document | Excel copy | Tabs | Contents |
|---|---|---|---|
| 01_PaymentGateway_Product_Discovery_v0.1.xlsx | [Open workbook](https://docs.google.com/spreadsheets/d/1WHGaFNZPu1YV9kfd4Xh4rP0Jql5jTZuc/edit?usp=drivesdk&ouid=116017105754217906634&rtpof=true&sd=true) | 9 | Stakeholders, interview backlog, BR, FR, NFR, MVP scope, questions, glossary |
| 02_PaymentGateway_Architecture_Assessment_v0.1.xlsx | [Open workbook](https://docs.google.com/spreadsheets/d/1HpOEcr1RNzmIV2gaMnxaPSY6uPznF5gU/edit?usp=drivesdk&ouid=116017105754217906634&rtpof=true&sd=true) | 9 | Assumptions, constraints, drivers, Risk Register, options, ADRs, domain ownership, traceability |
| 03_PaymentGateway_Delivery_Validation_v0.1.xlsx | [Open workbook](https://docs.google.com/spreadsheets/d/1PinjlMbPLhGaB9Q0eSP799K07-MyaCGa/edit?usp=drivesdk&ouid=116017105754217906634&rtpof=true&sd=true) | 8 | Workload model, PSP capability matrix, test plan, gates, roadmap, go-live checklist, change log |

## Review policy

1. Customer business inputs are discovery assumptions until an owner signs off. Do not treat 10,000/day as peak RPS, or 500 concurrent users as TPS.
2. Go-live 6 months, 2 PSPs, .NET 8/PostgreSQL/RabbitMQ/Redis are **workshop inputs / preferences**, not approved contractual constraints. ASP.NET Core 8 end-of-support must be reviewed before go-live.
3. Workshop exercises had **conflicting** peak-day volumes (30,000 vs 50,000) and different budgets. Financial limit, PSP choice, p95 SLO, RTO/RPO and legal custody scope remain TBD.
4. No live merchant data, secrets, cardholder data or production PSP credentials are stored here.
5. Excel workbooks include draft requirements, owned question backlog, architecture alternatives, formula-driven risk scores, scenario-based throughput and evidence matrices. Formula outputs are **scenarios**, not measured capacities.
6. Changes to assumptions, NFRs, risks or ADRs require traceability and reviewer approval. `docs/` contains design templates that should be updated after these registers are reviewed.
7. The CSVs under `docs/01-requirement-discovery/register-snapshots` and `docs/02-architecture-assessment/register-snapshots` are **diff-friendly PR snapshots**, not live synchronized spreadsheets. Update them when revising the approved Excel files.

## Cross-workbook traceability

`BR-` (business) → `FR-` (functional) → `NFR-` (quality attribute) → `DRV-` (architecture driver) → `R-` (risk) → `ADR-` (decision) → `T-` (validation test) → `GATE-` (approval).

## Next discovery priorities

1. Confirm **funds custody and settlement boundary** with Business Sponsor, Finance, and legal/compliance team.
2. Confirm actual PSPs, technical API contracts and VietQR partner/confirmation flows.
3. Confirm whether 10,000/day is business payments or HTTP requests and establish peak/arrival curve.
4. Agree payment success semantics, retry/idempotency, dispute/refund/reconciliation ownership.
5. Approve budget, measurable SLOs, RTO/RPO, team capacity, go-live criteria and MVP exclusions.

**No production architecture proposal should be approved before these dependencies have owners and evidence.**

## Current Day 1 draft — Product Discovery v0.2

- **Editable Excel:** [01_PaymentGateway_Product_Discovery_v0.2.xlsx](https://docs.google.com/spreadsheets/d/1NG0C3T2jWS5pj3Kcuvf6vfQVbB2kKY6Z/edit?usp=drivesdk&ouid=116017105754217906634&rtpof=true&sd=true)
- **Version:** v0.2; status: **in discovery — not approved**.
- **Original 9 tabs preserved**, plus: Decision Board, Money Flow RACI, Payment E2E Journey, Failure Acceptance, Discovery Gate, Day 1 Review Dashboard.
- **Open decisions:** 12/12. **Open approval gates:** 13/13. **Failure scenarios prepared:** 16/16. All counts reflect the draft baseline, not customer confirmation.
- **Review note:** [Business Boundary Review v0.2](./business-boundary-review-v0.2.md).
- **Workshop script:** [Customer Review Agenda](../01-requirement-discovery/day01-customer-review-agenda.md).
- **GitHub review snapshots:** [Decision Board](../01-requirement-discovery/register-snapshots/decision-board-v0.2.csv), [Failure Cases](../06-architecture-validation/register-snapshots/failure-acceptance-v0.2.csv).

Do not modify old v0.1 files in place without explicit version-control intent. Excel and CSV snapshots are not automatically synchronized.

## Current end-to-end architecture workbook (v1.0)

- [**PaymentGateway_E2E_Architecture_Product_Pack_v1.0.xlsx**](https://docs.google.com/spreadsheets/d/1d4ab3DIkGC_BYZUiJ34SSCEWjXENAC8U/edit?usp=drivesdk&ouid=116017105754217906634&rtpof=true&sd=true) — 42 tabs: 15 Day 1 tabs preserved + 27 additional E2E tabs.
- [GitHub E2E Blueprint](../08-e2e-blueprint/README.md) — diagrams, requirements traceability, architecture proposal, correctness, data, security, capacity, cost, tests and delivery.
- **Status:** Draft / pending customer, PSP, Finance, Legal, Security, SRE and architecture approvals.
- Workbook is stored as original **.xlsx** on Google Drive. GitHub contains readable Markdown and CSV snapshots. GitHub CSV snapshots are *not* automatically synchronized to the workbook.
- The previous 3 workbook files and Product Discovery v0.2 remain for historical comparison; do not overwrite them by assumption.

## Current DDD Design Pack v1.1 (additional Day 2 depth)

- **Editable Excel:** [PaymentGateway_E2E_Architecture_DDD_v1.1.xlsx](https://docs.google.com/spreadsheets/d/1WfbSCEKQRXnYwJvuz7XT4Q_gZ5v0QPwt/edit?usp=drivesdk&ouid=116017105754217906634&rtpof=true&sd=true) — 51 sheets: all prior 42 E2E tabs retained, 9 new detailed DDD tabs. Original v1.0 stays available as historical baseline.
- **GitHub:** [Strategic DDD documentation](../03-domain-design/README.md), [Context Map](../03-domain-design/02-context-map-and-contracts.md), [Refund ADR-0013](../04-solution-proposal/adrs/ADR-0013-refund-boundary-and-reservation.md).
- **Status:** 8 BCs proposed, 14 DDD review gates open. Financial custody, PSP contracts and Refund consistency boundary remain **Needs Confirmation**.
- Excel workbook is hosted on Drive and GitHub CSV/Markdown are review snapshots, not an automatic live sync.
