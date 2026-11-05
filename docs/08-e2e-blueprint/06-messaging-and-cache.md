# 06 — RabbitMQ + Transactional Outbox + Redis

## Why MQ is in a candidate architecture

Payment state changes, merchant notifications, PSP inquiries and reconciliation can take different time and recovery paths. RabbitMQ is a proposed asynchronous transport; it is NOT a financial source of truth and does not guarantee exactly-once external PSP charges.

## Candidate queues

| Logical exchange / queue | Message | Worker | Retry / DLQ | Dedup |
|---|---|---|---|---|
| payment.events → payment.dispatch.q | PaymentRequested | Payment Worker | Bounded backoff, poison event DLQ | Payment attempt + event ID |
| payment.events → notification.q | PaymentSucceeded/Failed/Unknown | Merchant Delivery | Delivery attempts, DLQ | Stable merchant event ID |
| ops.events → reconciliation.q | ReconciliationRequested | Recon Worker | File/run idempotency, operator review | Reconciliation run ID |
| integration.inbox | Authenticated PSP callbacks | PSP Webhook Worker | Durable receipt + bounded processing | PSP + provider event ID |

### Reliability contract

1. DB writes state and outbox in a single local transaction.
2. Outbox publisher sends with **publisher confirms** and stores delivery progress.
3. RabbitMQ may redeliver; consumers use manual acknowledgements and idempotency.
4. A message ack follows successful durable handling, not mere receipt.
5. Poison messages go to DLQ after a finite retry policy.
6. Retry schedules must be bounded and avoid triggering unsafe PSP side effects.
7. MQ backlog, oldest age, unacked count and outbox lag are SLO indicators.

RabbitMQ documentation: https://www.rabbitmq.com/docs/confirms and https://www.rabbitmq.com/docs/reliability

## Redis policy

| Key / data | Proposed use | Safeguard |
|---|---|---|
| merchant settings | Cache-aside metadata | Versioned invalidation / TTL; DB fallback |
| provider method capabilities | Provider configuration | Short TTL + explicit invalidation |
| routing rules | Read optimization | Config version; stale rule impact assessed |
| merchant rate limits | Protective policy | Controlled fallback on outage |
| payment status | Avoid authoritative caching | Critical stale outcome risk |
| idempotency | Never solely in Redis | PostgreSQL unique + transactional claim |

No raw secrets, unencrypted cardholder data or PII-heavy event bodies in MQ/cache. Capacity and failure policies are open.
