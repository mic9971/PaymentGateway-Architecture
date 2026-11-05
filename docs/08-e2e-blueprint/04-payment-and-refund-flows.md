# 04 — End-to-End Payment, Timeout, Webhook and Refund Sequences

These sequences are **illustrative contracts**. External PSP APIs, bank/VietQR settlement confirmation, and timeout behavior require contract tests.

## Payment creation: asynchronous candidate

~~~mermaid
sequenceDiagram
    autonumber
    participant M as Merchant
    participant A as Payment API
    participant DB as PostgreSQL
    participant O as Outbox Publisher
    participant Q as RabbitMQ
    participant W as Payment Worker
    participant PSP as PSP / Bank

    M->>A: POST /payments + Idempotency-Key
    A->>DB: TX atomic idempotency claim + Payment/Attempt + Outbox
    DB-->>A: Commit
    A-->>M: 202 Pending + PaymentId
    O->>DB: Claim unpublished outbox rows
    O->>Q: Publish PaymentRequested (confirm)
    Q-->>O: Publisher confirm
    O->>DB: Mark publish progress
    Q->>W: Deliver message
    W->>DB: Claim attempt and dedup event
    W->>PSP: Create payment (stable provider key if supported)
    alt Definitive PSP success
      PSP-->>W: Verified acceptance / transaction result
      W->>DB: Commit valid state transition + outbox
    else PSP definitive decline
      PSP-->>W: Declined
      W->>DB: Commit Failed + outbox
    else Timeout or ambiguous response
      W->>DB: Commit Unknown + inquiry schedule
    end
    W-->>Q: Ack only after durable handling
~~~

The exact HTTP response and API semantics must match customer checkout expectations. A PSP result indicating QR generated or customer action pending does NOT prove money paid.

## PSP webhook and merchant notification

~~~mermaid
sequenceDiagram
    autonumber
    participant PSP as PSP
    participant API as Webhook Endpoint
    participant Inbox as PostgreSQL Inbox
    participant Handler as Webhook Worker
    participant Pay as Payment State Machine
    participant Out as Outbox
    participant MQ as RabbitMQ
    participant Notify as Merchant Delivery Worker
    participant Merchant as Merchant Webhook

    PSP->>API: Callback + provider signature
    API->>API: Verify signature, replay window and schema
    alt Invalid signature or replay
      API-->>PSP: Reject per provider contract
    else Verified
      API->>Inbox: Insert unique (provider_id, event_id)
      Inbox-->>API: Durable acceptance
      API-->>PSP: Acknowledge per provider contract
      Inbox->>Handler: Dispatch verified event
      Handler->>Pay: Validate correlation and state transition
      Pay->>Out: Persist result + notification event (same transaction)
      Out->>MQ: Publish with confirm
      MQ->>Notify: Deliver
      Notify->>Merchant: Signed event_id callback
      alt Merchant endpoint unavailable
        Notify->>Notify: Bounded retry + durable delivery record
      end
    end
~~~

PSP callback ordering and provider event identifiers may be different for each integration. Inbox can deduplicate an event, not guarantee PSP created only one charge.

## Unknown outcome: deliberately safe recovery

~~~mermaid
flowchart TD
    A["PSP request timed out"] --> B["Persist UNKNOWN and attempt reference"]
    B --> C{"Provider supports inquiry?"}
    C -->|Yes| D["Query by stable provider reference"]
    C -->|No| E["Finance escalation / reconciliation"]
    D --> F{"Authoritative outcome?"}
    F -->|Paid| G["Verify + mark SUCCEEDED"]
    F -->|Declined/no financial effect proven| H["Mark FAILED if policy allows"]
    F -->|Still unclear| E
    E --> I["Wait for verified statement / event or investigate"]
    I --> G
    I --> H
~~~

**Forbidden design:** catch a timeout from PSP A, immediately call PSP B to collect the same payment. Unknown is not Failed.

## Refund (separate workflow)

~~~mermaid
sequenceDiagram
    autonumber
    participant M as Merchant
    participant API as Refund API
    participant DB as PostgreSQL
    participant W as Refund Worker
    participant PSP as PSP
    participant R as Recon
    M->>API: POST /payments/{id}/refunds + refund idempotency
    API->>DB: TX check merchant / remaining refundable; reserve amount; refund + outbox
    DB-->>M: Refund Pending
    W->>DB: Claim refund attempt
    W->>PSP: Refund request with stable reference if supported
    alt Definitive result
      PSP-->>W: Refund success/decline
      W->>DB: Commit verified refund state
    else Ambiguous result
      W->>DB: Refund UNKNOWN
      R->>PSP: Inquiry/reconciliation
      R->>DB: Persist evidence and resolved state
    end
~~~

Total successful + in-flight reserved refundable amount must never exceed original permitted refunded amount. Exact release of reserved funds on definitive failure needs Finance/PSP contract definition.

## Failure scenarios to demonstrate

Concurrent idempotency, worker kill after PSP call, ack lost, publisher crash, forged/reordered webhook, late QR transfer, refund race, merchant callback outage, PSP reconciliation mismatch, PostgreSQL failover.
