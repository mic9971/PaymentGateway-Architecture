# PSP Webhook Security & Reconciliation v0.4

**Status:** Development-only Mock PSP implementation; architecture review pending. No real financial integrations or customer approvals.

## Design goals

1. Authenticate the **raw callback body** against a provider-specific HMAC signature **before** it reaches any state-changing application service.
2. Reject timestamp replay (demo clock skew ±5 minutes), malformed data, wrong provider IDs, mismatched amount/currency and wrong provider operation key.
3. Persist a unique \`(provider_id,event_id)\` receipt. Duplicate callback produces **one** Payment transition and one Outbox event.
4. Never regress or overwrite an existing terminal Payment state because a contradictory signed callback arrives later; save the received event with disposition \`ConflictingFinal\` for investigation.
5. Keep Reconciliation responsible for **discrepancy records**, while **Payment** owns authoritative gateway-state transitions.
6. Preserve the existing read-only **Unknown Status Inquiry** worker and no-blind-charge-retry rule.

## End-to-end design

~~~mermaid
sequenceDiagram
  autonumber
  participant PSP as Mock PSP
  participant API as Webhook Endpoint
  participant V as HMAC + Replay Guard
  participant I as Mock Webhook Processor
  participant DB as PostgreSQL
  participant R as Reconciliation Worker
  participant Outbox as Outbox Publisher

  PSP->>API: POST raw JSON + timestamp + HMAC
  API->>V: Verify signature, skew, payload bounds
  alt Invalid signature
    V-->>API: 401 Reject
  else Signature valid
    API->>I: ApplyVerifiedAsync
    I->>DB: TX lock Attempt then Payment
    I->>DB: Correlate operation, amount, currency
    alt First authorized event and valid transition
      I->>DB: Inbox event + Payment/Attempt update + Outbox
    else Duplicate event ID
      I->>DB: No additional state update
    else Conflicting terminal state
      I->>DB: Store event disposition ConflictingFinal
    end
    DB-->>API: Commit
    API-->>PSP: 200 / 202 disposition
  end
  R->>DB: Compare Mock PSP ledger vs Payment state
  R->>DB: Upsert separate discrepancy and close resolved
  DB->>Outbox: Publish eligible events
~~~

The Mock PSP HMAC protocol is a **deliberate local teaching contract**, not the VietQR, e-wallet, or PSP official signing scheme. This design does not authorize production callbacks.

## API development contract

\`POST /v1/providers/mock/webhook\`

- \`X-Mock-Timestamp\`: Unix epoch seconds, accepted within ±300 seconds of gateway UTC.
- \`X-Mock-Signature\`: \`sha256=<64 lowercase hexadecimal bytes>\`; HMAC-SHA256 over \`timestamp + "." + exact raw HTTP body\`.
- Secret: \`MockPsp:WebhookSecret\` from local environment, at least 16 UTF-8 bytes (demo policy, not production key strength guidance).
- JSON: \`event_id\`, \`payment_id\`, \`attempt_id\`, \`provider_operation_key\`, \`amount_minor\`, \`currency\`, \`status\`.
- \`status\` accepts \`Succeeded\` or \`Failed\` **only as simplified Mock PSP finality**.
- Payload cap: 16 KiB.
- Response: \`Applied\`, \`Duplicate\`, \`AlreadyFinal\`, \`ConflictingFinal\` (202 investigation) or rejected.
- The endpoint exists **only in Development**, because the entire demo API fails startup in non-Development environments.

### Signing a local test request

Use the **same raw bytes** for both signature computation and POST. Example pseudocode:

~~~text
raw = UTF8(JSON.stringify(demo_event))
timestamp = UTC Unix epoch seconds
signature = "sha256=" + lowercase_hex(HMACSHA256(local_test_secret,
                                    UTF8(timestamp + ".") + raw))
POST raw body with X-Mock-Timestamp and X-Mock-Signature
~~~

**Never expose the synthetic secret through a public test signing endpoint.** A valid local signature is not proof of bank settlement.

## Ownership and invariants

| BC | Responsibility | Implemented location | Boundary caveat |
|---|---|---|---|
| Provider Integration (ACL) | Webhook authenticity, event identity, callback normalization | \`integration.mock_webhook_inbox\` | For demo, processor resides physically in \`Payments.Infrastructure\`; split by module before production |
| Payment (Core) | Validating state transitions and Payment/Attempt writes | \`payment.*\` | Current demo processor uses direct SQL; replace with reviewed Payment application command contract |
| Reconciliation | Compare PSP evidence with Payment and log mismatch | \`reconciliation.mock_discrepancies\` | Reads local fake PSP ledger; no bank/statement reconciliation |
| Messaging infrastructure | Outbox event publication + Inbox receipt | \`messaging.*\` | At-least-once delivery; duplicates may appear |

The current implementation favors a verifiable **single local DB transaction** for security/correctness tests over the desired long-term module separation. **ADR-0019 documents this intentional architecture debt**; do not mistake a code namespace for an approved context boundary.

## Automated verification

[GitHub Actions evidence — 25/25 tests passed](https://github.com/mic9971/PaymentGateway-Architecture/actions/runs/42054657420): 3 unit + 22 integration on PostgreSQL/RabbitMQ CI services.

Six new tests: raw-body HMAC/tampering/stale clock, signed valid transition, concurrent duplicate event identity, contradictory final event, amount/provider-key mismatch, and read-only reconciliation discrepancy. These test the validator and processor directly; **no full HTTP route integration test** has been run.

## Runbook

From \`deploy/compose\`, for a fresh local volume:

~~~bash
cp .env.example .env
# Set separate local-only POSTGRES_PASSWORD, RABBITMQ_PASSWORD and MOCK_WEBHOOK_SECRET.
docker compose up -d --build
~~~

For an **existing** PostgreSQL volume, initialization scripts do not rerun. Apply new v0.4 schema exactly once:

~~~bash
docker compose exec -T postgres psql -U payment_demo -d payment_demo < postgres-init/004_mock_webhooks_reconciliation.sql
~~~

Create a \`MOCK\` payment, read \`attempt_id\` and \`provider_operation_key\` from \`payment.payment_attempts\`, prepare the signed callback with identical amount/currency, and send it to \`http://127.0.0.1:8080/v1/providers/mock/webhook\`.

Observe:

~~~sql
SELECT event_id, payment_id, outcome, disposition, received_at
FROM integration.mock_webhook_inbox ORDER BY received_at DESC LIMIT 20;

SELECT provider_operation_key, provider_outcome, gateway_outcome, resolved_at
FROM reconciliation.mock_discrepancies ORDER BY detected_at DESC LIMIT 20;

SELECT event_type, published_at FROM messaging.outbox_messages
ORDER BY occurred_at DESC LIMIT 20;
~~~

Do not expose \`X-Demo-Merchant-Id\`, test callback signing secret, API or MQ management interface to public traffic.

## Validation gaps — must remain open

- Actual bank/PSP callback signature scheme and key rotation, merchant account scope, replay tolerance, event ordering and webhook retry guarantees.
- End-to-end HTTP handler test, multi-node concurrency and database deadlock/failover testing.
- Gateway state transition conflict escalation and authenticated Finance/Ops investigation workflow.
- Real bank statement, refund and VietQR late-transfer reconciliation.
- PSP success/settlement/capture finality and custody/legal scope.
- Threat modeling, rate limiting, security audit, RTO/RPO, SLO and operational cost.

## Excel and PR

- [Implementation Tracker v0.4 — 21 sheets](https://docs.google.com/spreadsheets/d/1-KWKpiXq7nJyy0ItsacSh-tJRvRXy8OE/edit?usp=drivesdk&ouid=116017105754217906634&rtpof=true&sd=true)
- [ADR-0019](../04-solution-proposal/adrs/ADR-0019-verified-webhook-reconciliation.md)
- Draft PR #5 on \`feature/psp-webhook-reconciliation\`, based on Draft PR #4. No merges authorized.
