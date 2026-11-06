# Financial Authorization & Transactional Refund Audit — v0.8

**Status:** Development-only Mock PSP experiment. Finance controls, evidence retention and high-value policy are **not approved**. Zero real-money transactions.

## Business problem

A refund may exceed a threshold requiring independent authorization. A valid JWT with a refund permission is insufficient proof that two different people reviewed the same refund, especially if the requester also controls an approval credential. At the same time, the Refund Worker must not initiate the external PSP refund before approvals are fully committed.

## Candidate policy (not a business requirement)

| Item | Development value | Approval status |
|---|---|---|
| Currency | VND minor units | Demo only |
| High-value boundary | amount >= **1,000,000 VND** | **Finance TBD** |
| Minimum positive approvals | **2 distinct signed `sub` values** | **Finance/Security TBD** |
| Separation of duties | Requester `sub` cannot approve own Refund | Implemented in demo |
| Finance authority | JWT `refunds:approve` AND live IAM registry grant | Implemented in demo |
| Refund processing | Only state `Requested` is picked up by Worker | Implemented in demo |
| Budget handling | `PendingApproval` retains existing atomic reservation | Implemented in demo |
| Refund reversal | No automatic release on timeout or Unknown | Prior Mock slice |

A signed identity claim is **not proof of a unique human**. The local mint script can generate different synthetic subjects; no independent workforce IdP or high-value business approvals are connected.

## End-to-end state flow

~~~mermaid
stateDiagram-v2
  [*] --> PendingApproval : High-value Request + budget reserve
  PendingApproval --> PendingApproval : First distinct approver
  PendingApproval --> Requested : Second independent approver
  Requested --> Processing : Durable worker lease
  Processing --> Succeeded : Verified PSP mock
  Processing --> Failed : Definitive mock failure
  Processing --> Unknown : Ambiguous PSP timeout
  Unknown --> Succeeded : Read-only inquiry
  Unknown --> Failed : Verified definitive no-effect outcome
~~~

Small-value refunds continue `Requested` immediately, preserving earlier semantics.

## Bounded Context ownership

- **Merchant Identity/IAM** maintains active merchant, enabled client, issuer/subject/client_id binding and grant `refunds:approve`.
- **Refund** owns `refund.refund_requests` and `refund.refund_approvals`, separates requester vs approvers and changes PendingApproval→Requested.
- **Financial Audit (prototype)** owns `finance_audit.refund_actions`. Refund calls this journal within its transaction. Updates/deletes are rejected by a trigger for ordinary SQL mutations, but a privileged DB owner can bypass it.
- **Messaging** owns Outbox. `RefundRequested` appears only once after second approval, and `RefundApprovalRecorded` can be emitted on the first approval.
- **PSP Integration** remains Mock-only, invoked only by the Refund Worker after durable release. It never acts on a PendingApproval refund.

Current modules are colocated in one PostgreSQL database. IAM checks are repeated **inside** the approval transaction and locked while recording a decision. Production extraction across independently deployed contexts would require a dedicated authorization/consistency design.

## Transactional approval algorithm

1. API validates a signed JWT and requires `refunds:approve`.
2. A row-level lock on the Refund serializes concurrent approvers; only `PendingApproval` may proceed.
3. The approver `sub` must differ from the recorded requester `sub`.
4. Under that same transaction, query IAM merchant Active and client granted `refunds:approve`, taking shared locks on entitlement rows.
5. Insert `refund.refund_approvals` with unique key `(refund_id,actor_subject)`. A repeated actor cannot count twice.
6. Append `RefundApprovalRecorded` in the finance action journal.
7. On the second distinct approval, atomically change Refund to `Requested`, append `RefundReleased` journal event and one `RefundRequested` Outbox event.
8. Commit. Only then can the Refund Worker claim the request.

No external PSP network call is made inside a financial or approval DB transaction. The original RefundBudget remains reserved throughout PendingApproval.

## Development API contract

`POST /v1/refunds/{refundId}/approvals`

Requires a signed merchant bearer token with `refunds:approve`, matching active merchant/registered client and a distinct finance actor subject. No request body may nominate the approver.

- 401: absent/invalid JWT.
- 403: signed token lacks operation scope or live IAM grant.
- 404: wrong merchant / unknown Refund.
- 409: not PendingApproval, requester self-approval or duplicate actor.
- 202: first distinct approval recorded, still PendingApproval.
- 200: second distinct approval recorded, Refund released to Requested.

## Local demonstration

From `deploy/compose`, start the Mock stack. Apply migration 007 to an **existing** PostgreSQL volume:

~~~bash
docker compose exec -T postgres psql -U payment_demo -d payment_demo \
  < postgres-init/007_refund_approvals_finance_audit.sql
~~~

Register two **synthetic** finance client identities for a previously seeded active merchant, using two independent `sub` values:

~~~sql
INSERT INTO iam.merchant_clients
(merchant_id,issuer,subject,client_id,granted_scopes,enabled)
VALUES
('11111111-1111-4111-8111-111111111111',
 'https://merchant-auth.local.test','finance-a','finance-client-a',
 ARRAY['refunds:approve']::text[],true),
('11111111-1111-4111-8111-111111111111',
 'https://merchant-auth.local.test','finance-b','finance-client-b',
 ARRAY['refunds:approve']::text[],true);
~~~

With the **same local DEVELOPMENT-only JWT signing key** set in the Compose stack:

~~~bash
export MERCHANT_ID='11111111-1111-4111-8111-111111111111'
TOKEN_A=$(python3 scripts/security/mint_mock_merchant_jwt.py \
 --merchant "$MERCHANT_ID" --actor-subject finance-a \
 --client-id finance-client-a --scopes refunds:approve)
TOKEN_B=$(python3 scripts/security/mint_mock_merchant_jwt.py \
 --merchant "$MERCHANT_ID" --actor-subject finance-b \
 --client-id finance-client-b --scopes refunds:approve)

curl -i -X POST "http://127.0.0.1:8080/v1/refunds/$REFUND_ID/approvals" \
 -H "Authorization: Bearer $TOKEN_A"
curl -i -X POST "http://127.0.0.1:8080/v1/refunds/$REFUND_ID/approvals" \
 -H "Authorization: Bearer $TOKEN_B"
~~~

The Refund must first be created on a synthetic `Succeeded` Payment and meet the high-value threshold. Validate:

~~~sql
SELECT refund_id,amount_minor,state,approval_required,request_actor_subject
FROM refund.refund_requests ORDER BY created_at DESC LIMIT 20;

SELECT refund_id,actor_subject,recorded_at
FROM refund.refund_approvals ORDER BY recorded_at DESC LIMIT 20;

SELECT refund_id,action_kind,actor_subject,occurred_at
FROM finance_audit.refund_actions ORDER BY occurred_at DESC LIMIT 20;

SELECT aggregate_id,event_type FROM messaging.outbox_messages
WHERE event_type LIKE 'Refund%' ORDER BY occurred_at DESC LIMIT 20;
~~~

**Never reuse these demo credentials for real funds.** No public approve/deny administration or rejection/release flow exists.

## Verification

[GitHub Actions — 48/48 tests passed (3 unit + 45 integration)](https://github.com/mic9971/PaymentGateway-Architecture/actions/runs/42174577801), with real PostgreSQL/RabbitMQ CI containers and Mock PSP only.

- FIN-T01: high-value PendingApproval not claimable until two distinct approvals, one processing Outbox event.
- FIN-T02: requester self-approval and cross-merchant approval denied.
- FIN-T03: DB grant revocation blocks an approver inside the transactional check.
- FIN-T04: 12 concurrent approval calls release only once.
- FIN-T05: trigger rejects updates/deletes to action journal.
- FIN-T06: actual ASP.NET Core HTTP auth + IAM scope + approval responses with two finance identities.

## Non-goals and remaining production gates

- **No independently verified identity of human approvers**, segregation-of-duties policy, or formal dual control.
- Threshold 1,000,000 VND is an illustrative configuration embedded in code, not an approved rule or fee/settlement-aware calculation.
- No Approval **Rejection/Expiration/Cancel** workflow, denied refund budget release or escalation SLA.
- No actor role provisioning/approval lifecycle, impersonation defense beyond existing Mock IAM, or refund fraud scoring.
- No immutable/tamper-evident off-DB journal, signature chain, WORM archive, SIEM ingestion or independent audit attestation.
- No real PSP refund, high-value approval SLA, failover/restore, real settlement reconciliation or production readiness.

**ADR-0023 remains Proposed. Do not merge or deploy real-money endpoints.**
