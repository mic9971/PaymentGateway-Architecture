# 07 — PostgreSQL ↔ RabbitMQ Reliability: Outbox, Inbox and Work Claims

## Main rule

**Persist business state and publication intent together in PostgreSQL.** Publish to RabbitMQ outside the transaction. Handle redelivery. Do not assume RabbitMQ is the source of payment truth.

## Transaction sequence

~~~mermaid
sequenceDiagram
    autonumber
    participant M as Merchant
    participant API as Payment API
    participant DB as PostgreSQL
    participant Pub as Outbox Publisher
    participant MQ as RabbitMQ
    participant W as Payment Worker
    participant PSP as PSP

    M->>API: POST /payments + idem-key
    API->>DB: TX unique claim + Payment + Attempt + Outbox
    DB-->>API: COMMIT
    API-->>M: PaymentId / Pending
    Pub->>DB: Claim unpublished outbox rows
    Pub->>MQ: Publish event (publisher confirms)
    MQ-->>Pub: Confirm
    Pub->>DB: Mark as published
    MQ->>W: Deliver PaymentRequested
    W->>DB: Durable attempt claim
    W->>PSP: Provider operation with stable key
    alt Definitive response
      PSP-->>W: Verified result
      W->>DB: TX persist state + evidence + outbox
    else Ambiguous timeout
      W->>DB: TX persist UNKNOWN + inquiry schedule
    end
    W-->>MQ: Ack only after durable safe handling
~~~

If a worker crashes after contacting PSP before persisting outcome, it must **not** blindly call PSP again when message is redelivered. The recovery protocol is documented in [Provider Recovery](./06-provider-recovery.md).

## Outbox schema and claim pattern (conceptual)

Columns: \`outbox_id\`, \`event_id\`, \`aggregate_id\`, \`event_type\`, \`schema_version\`, \`payload_minimal\`, \`created_at\`, \`available_at\`, \`claimed_until\`, \`published_at\`, \`attempt_count\`.

Publisher work claiming can use \`SELECT ... FOR UPDATE SKIP LOCKED\` in a **short transaction** over a queue-like table. PostgreSQL warns that SKIP LOCKED yields an inconsistent view and is unsuitable for general-purpose read consistency. (https://www.postgresql.org/docs/current/sql-select.html)

- Claim a bounded batch with lease/claim marker.
- Commit claim; publish to durable queue with publisher confirms.
- Store published status after positive confirm.
- Crash between confirm and mark causes **possible duplicate publish** — by design consumers must dedup.
- Track oldest unpublishable event, lag, retries and poison/DLQ.

## Consumer inbox and financial side effects

- \`(consumer_name,event_id)\` unique receipt suppresses duplicate local event processing.
- Manual acknowledgment occurs after relevant durable state is saved.
- For an external PSP call, **inbox receipt alone is insufficient**: it may exist despite lost PSP response, or may not exist even after PSP charged.
- Use PaymentAttempt + stable PSP reference + inquiry/idempotency protocol and persisted Unknown outcome.
- Short lock/lease or fencing token protects local state coordination; it is **not** a PSP-side fencing guarantee.

## Webhook flow is a different inbox

Provider Integration must verify a provider-specific signature/replay condition **before** treating the content as trusted. Persist verified receipt with provider/event identity (if contract guarantees unique IDs), ack per PSP contract, process state with version guards.

Merchant outbound webhook is separate: use event ID, signature/verification scheme, retry policy, delivery history, endpoint allowlisting and replay controls.

## RabbitMQ semantics

RabbitMQ **publisher confirms** and **consumer acknowledgments** are separate durability/ownership mechanisms. Both matter. Consumer acknowledgments support at-least-once delivery and messages may redeliver; exactly-once external PSP charges are an **application/provider protocol concern**.

Official docs:
- https://www.rabbitmq.com/docs/confirms
- https://www.rabbitmq.com/docs/reliability

## Operational checks

- Outbox unpublished age, publish retries, RabbitMQ queue ready/unacked, DLQ count and consumer lag.
- Worker PSP timeout/unknown rates and average time to resolution.
- PostgreSQL connections, locks, TPS, IO and partial-index write churn.
- Proof that financial safety survives DB/MQ/worker failover and test code that injects failures.
