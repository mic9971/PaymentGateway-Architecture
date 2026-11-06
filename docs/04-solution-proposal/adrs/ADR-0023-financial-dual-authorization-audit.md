# ADR-0023 — Two-Person Refund Authorization and Durable Financial Event Evidence

- **Status:** Proposed / Development-only experiment.
- **Date:** 2026-11-06.
- **Owner:** Finance, Security/IAM, Solution Architect, Operations.
- **Related:** ADR-0020 RefundBudget; ADR-0021/0022 Merchant IAM.

## Drivers

High-value refunds can be initiated with valid merchant credentials but require independent authorization before an external financial effect. Concurrent approvals, duplicate requests and revoked approver rights must not release a Refund twice. Decision evidence must be recorded durably with the Refund state transition.

## Options

A. Caller JWT with `refunds:create` alone; process immediately — **Rejected for candidate high-value policy** because one credential controls initiation and release.

B. Two independent finance actor approvals before transitioning `PendingApproval`→`Requested` — **experimentally implemented**. A unique `(refund_id,actor_subject)` key, requester exclusion, and Refund row lock guard concurrency.

C. External approval/authorization product with strong workforce identity, step-up, four-eyes controls and separately attested audit — **production candidate** pending business, PSP and compliance requirements.

## Experimental decision

Use B for Mock-only case. The default high-value threshold is **1,000,000 VND minor units** for education only. Refund is reserved atomically on creation and held PendingApproval; worker claims only Requested.

The two checks are a distinct **signed `sub`** and a matching **live IAM registry-granted `refunds:approve`** operation, revalidated under the approval transaction. The application writes each decision and the second-approver release to an append-only-by-trigger PostgreSQL journal in the same transaction as Refund/Outbox.

## Explicit limitations

- Signed identities in local scripts are **synthetic; two distinct subs do not prove two distinct people**. The dev HMAC key holder could mint both, so this cannot satisfy real segregation-of-duties.
- An SQL trigger prevents ordinary UPDATE/DELETE, **not** privileged database administrator/superuser modification. The journal is not an immutable independent ledger.
- Failure to write the journal rolls back approval release. Existing non-approval Provider results and older Refund actions still require a full regulated finance audit design.
- No configured refusal/expiration route, release-of-reservation policy, escalation or chargeback path.
- No real staff IdP, MFA, independent approval device, revocation SLA, fraud, KYC or authorization sign-off.

## Verification and review conditions

[48/48 mock CI tests](https://github.com/mic9971/PaymentGateway-Architecture/actions/runs/42174577801) include concurrency, requester exclusion, revocation, HTTP scope and audit-update rejection; does not certify production controls.

Approve only after:
- Finance signs amount/currency rules, approval levels, independent person model, rejection/expiry and settlement policy.
- IAM/Security verifies workforce IdP, subject uniqueness, client segregation, MFA/step-up, grant lifecycle and anti-impersonation.
- Audit/Compliance defines independent tamper-evident evidence, retention, access and reconciliation with journal/external statements.
- SRE/QA run process-kill/DB failover/authorization races and load tests.
- Architect decides whether Refund authorization remains in the Refund BC or becomes a dedicated Financial Authorization bounded context.

**No PR merge or production release is authorized by this ADR.**
