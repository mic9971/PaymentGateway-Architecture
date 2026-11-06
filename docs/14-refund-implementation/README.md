# Refund Domain & Atomic RefundBudget v0.5

**Status: Development-only Mock PSP, design experiment; no real refunds, no regulatory or business approval.**

## Architecture decision

This slice implements a **candidate of ADR-0013 Option A**: the Refund Bounded Context owns an atomic RefundBudget row keyed by PaymentId. At creation time, it takes a short PostgreSQL row lock on an authenticated, \`Succeeded\` Payment and initializes the verified ceiling from its fixed amount. It then claims refund idempotency, reserves the amount with a conditional update, writes a RefundRequest and a RefundRequested Outbox in the **same database transaction**.

This enforces the limited demo invariant:

\`reserved_minor + completed_minor <= verified_limit_minor\`

It **does not prove real financial entitlement**; production may need settlement/capture-adjusted limit, FX, fees, disputes, reversals, chargebacks, payout and cross-provider policy. Actual money-keeping/custody responsibility must be signed off. If payment corrections can change the ceiling, design a synchronized mutation protocol before production use.

## Context and transaction boundaries

- Payment BC owns \`payment.payment_intents\` and its state. Refund may read and lock a verified Payment row **as a documented co-located transactional exception**. Refund does **not** mutate Payment state.
- Refund BC owns \`refund.refund_requests\`, \`refund.refund_budgets\`, Refund workflow, provider operation identity and reserved/completed totals.
- Mock Provider records refund outcome in \`refund.mock_provider_refunds\` in an **independent transaction**, but on the **same local PostgreSQL host**. No bank/PSP call.
- Messaging owns \`messaging.outbox_messages\` and RabbitMQ at-least-once delivery. \`Refund*\` events are routed to \`refund.*\` and deduplicated in the demo audit consumer.
- If a refund attempt is \`Unknown\`, the budget remains **reserved**. It becomes completed only after verified success; it is released only after verified definitive failure.

## Refund lifecycle

~~~mermaid
stateDiagram-v2
  [*] --> Requested
  Requested --> Processing : Durable mock worker claim
  Processing --> Succeeded : Verified mock provider result
  Processing --> Failed : Definitive no-effect mock failure
  Processing --> Unknown : Timeout or worker lease expires
  Unknown --> Succeeded : Read-only mock provider inquiry
  Unknown --> Failed : Verified definitive mock failure
~~~

Financial effect never happens inside the PostgreSQL authorization transaction. The worker sends one mock provider initiation after a durable claim. Unknown recovery **queries**, never re-initiates a refund.

## Public demo endpoints

- \`POST /v1/payments/{paymentId}/refunds\` with \`Idempotency-Key\`, \`X-Demo-Merchant-Id\`, body \`{"amountMinor":700000}\`.
- \`GET /v1/refunds/{refundId}\` with the same demo merchant header.

First request produces \`202\` and \`Requested\`; identical-key replay returns \`200\` with same RefundId. Different request for the same key yields \`409\`. Insufficient refundable budget or non-Succeeded Payment yields \`409\`. All endpoints are blocked from non-Development startup.

Merchant identity is a **fake development-only header**, not authentication. Refund GETs are tenant scoped in SQL.

## Concurrent example: 1,000,000 VND payment

Two concurrent refund requests: A=700,000 and B=600,000. Under a short transaction and conditional \`UPDATE\`, one reservation can commit; the other must fail if it would cause \`reserved+completed>1,000,000\`. Which wins is intentionally nondeterministic. The business may subsequently request the exact remaining balance through a **new Idempotency-Key**.

The DB \`CHECK\` constraint is a second line of defense against accidental overspend. No external PSP idempotency is claimed.

## Verification — green CI

[GitHub Actions 42153098364 — **32/32 passed: 3 unit + 29 PostgreSQL/RabbitMQ integration**](https://github.com/mic9971/PaymentGateway-Architecture/actions/runs/42153098364)

New tests RF-T01…RF-T07:

| ID | What it proves in the mock | Status |
|---|---|---|
| RF-T01 | Two concurrent refunds totaling 130% authorize at most 100% | Passed |
| RF-T02 | Reused idempotency key cannot double reserve | Passed |
| RF-T03 | Pending payment/other merchant cannot request refund | Passed |
| RF-T04 | Mock definitive failure releases the reservation | Passed |
| RF-T05 | PSP refund completed before response loss: Unknown holds, inquiry completes | Passed |
| RF-T06 | No provider evidence: Unknown still holds budget | Passed |
| RF-T07 | Concurrent inquiries finalize once and produce one completion Outbox event | Passed |

All are demo integration tests, not a provider certification.

## Development runbook

For **new** local database volume:

~~~bash
cd deploy/compose
cp .env.example .env
# Replace demo-only secrets and choose MOCK_REFUND_OUTCOME.
docker compose up -d --build
~~~

For existing PostgreSQL volumes, explicitly apply the new \`005\` script; Docker init scripts do not rerun:

~~~bash
docker compose exec -T postgres psql -U payment_demo -d payment_demo < postgres-init/005_refund_budget.sql
~~~

First create a Mock payment and wait until status \`Succeeded\`. Only then request refund. For a payment \`paymentId\`:

~~~bash
curl -i -X POST "http://127.0.0.1:8080/v1/payments/$PAYMENT_ID/refunds" \
 -H "Content-Type: application/json" \
 -H "X-Demo-Merchant-Id: $MERCHANT_ID" \
 -H "Idempotency-Key: refund-demo-001" \
 -d '{"amountMinor":700000}'
~~~

Inspect balance in the local database:

~~~sql
SELECT payment_id,verified_limit_minor,reserved_minor,completed_minor
FROM refund.refund_budgets ORDER BY payment_id;

SELECT refund_id,payment_id,amount_minor,state,provider_operation_key
FROM refund.refund_requests ORDER BY created_at DESC;

SELECT event_type,published_at FROM messaging.outbox_messages
WHERE event_type LIKE 'Refund%' ORDER BY occurred_at DESC;
~~~

To model refund after PSP effect with lost response, set \`MOCK_REFUND_OUTCOME=TimeoutAfterSuccess\`. Recovery should query the provider ledger and commit Unknown→Succeeded, not initiate another refund. \`TimeoutBeforeEffect\` leaves Unknown/reserved until evidence or Finance investigation.

## Critical limitations / blockers

1. **ADR-0013 remains Proposed**; this code demonstrates Option A rather than approving it.
2. Amount is assumed equal to eligible paid amount in VND; this is **not** a valid universal refund formula.
3. Lack of real PSP refund idempotency key guarantee and authoritative status inquiry.
4. Unknown refund cases after five inquiry attempts currently lack a full operator workflow/SLA.
5. No refunds from Merchant API may be deployed publicly without real authentication, authorization and abuse controls.
6. No partial capture, multiple currencies, provider ledger separation, dispute, chargeback or settlement corrections.
7. The SQL \`005\` is a local init/migration script, not approved production migration with rollback.
8. Real bank settlement, refund costs, long-running game days, RTO/RPO and SLO remain **Not Run**.

## Artifacts

- [Implementation Tracker v0.5 — 27 Excel sheets](https://docs.google.com/spreadsheets/d/1-bvaLzSISmrZNTg27gQIVRS4us8xEYxo/edit?usp=drivesdk&ouid=116017105754217906634&rtpof=true&sd=true)
- [ADR-0020 implementation decision](../04-solution-proposal/adrs/ADR-0020-refund-budget-mock.md)
- [ADR-0013 original alternatives](../04-solution-proposal/adrs/ADR-0013-refund-boundary-and-reservation.md)
- Draft PR #6 \`feature/refund-budget-concurrency\` → Draft PR #5 branch. No merges authorized.
