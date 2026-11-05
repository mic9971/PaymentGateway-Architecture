# ADR-0017 — Durable Outbox Publication and At-Least-Once Message Consumption

- **Status:** Proposed (not accepted).
- **Date:** 2026-11-05.
- **Contexts:** Payment integration events, Notifications, technical Messaging infrastructure.

## Problem

Payment DB commits can succeed while RabbitMQ is down; broker acknowledgements can succeed while DB fails to mark the message as published; consumers may process messages but crash before acknowledgements.

## Alternatives

A. DB polling for every asynchronous workflow: lower infrastructure cost, simpler operations; bounded fanout/consumer scalability.

B. PostgreSQL **Transactional Outbox** + RabbitMQ publisher confirms + durable queues + deduping consumer (implemented in local demonstration): supports asynchronous fanout and decoupling; incurs broker ops, duplicates and eventual consistency.

C. Publish directly to RabbitMQ in API transaction: **Rejected** because independent DB/broker transactions can lose integration events.

## Proposed decision

Demonstrate B for event delivery (Audit Projection). Keep the Mock PSP processor DB-polling until financial-side-effect safety is separately approved. The event publisher never sends or retries a PSP charge itself.

## Failure semantics

- Broker confirmed + DB mark failed → duplicate RabbitMQ delivery possible.
- Inbox committed + consumer ack lost → duplicate RabbitMQ delivery possible.
- EventId stable; Inbox unique key protects receiver's **local** side effects.
- Poison messages move to DLQ after finite delivery attempts; no silent loss.
- Outbox publishes at least once (not at most once), contingent on surviving DB and an operating recovery worker.
- Exactly-once financial PSP effects are **not** guaranteed by RabbitMQ or DB unique constraints.

## Costs and risks

RabbitMQ quorum queues add disk/replication and broker management; connection outages and routing changes can create lag. Operator needs alerts for oldest outbox, DLQ growth, receive failures and reconciliation.

## Validation plan

CI with PostgreSQL + real RabbitMQ; test successful confirms/queue delivery, duplicate Inbox event, malformed JSON rollback. Manual game days for broker outage, confirmation-gap replay, DLQ, and worker crash are still required before approval.

## Open decisions

Actual business consumers, event versioning/contract, retention, ordering and DLQ recovery authority. Approvers: Architect, Platform/SRE, Security, Finance (if financial side effects introduced).
