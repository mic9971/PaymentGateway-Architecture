# Manual Game-Day Recipes — Development Only

These are **manual steps**, not automatically completed tests. Do not run on production.

## Broker outage
From `deploy/compose`, stop RabbitMQ with `docker compose stop rabbitmq`; submit synthetic payments using `MOCK` only. Inspect PostgreSQL Outbox backlog, then restart with `docker compose start rabbitmq` and watch the Outbox and Inbox counts.

## Worker crash between mock-provider effect and Gateway commit
Use `MOCK_PSP_OUTCOME=TimeoutAfterSuccess` for the deterministic safe simulation. For a true process kill at exactly that boundary, instrument a **dev-only failure injection checkpoint**, then record trace and transaction evidence. This checkpoint is **not yet implemented**.

## DLQ / poison event
Create an invalid event on a dedicated test queue only after approving the broker-policy and security requirements. Existing tests cover JSON rejection but not full DLQ redrive or operator workflow.

## Evidence to capture
Build SHA, date, environment, fault start/end, messages ready/unacked, oldest Outbox age, EventId, PaymentId, result, recovery duration, anomalies and reviewer sign-off.

**Current test coverage:** automated integration tests cover simulated publisher outage/confirm gap, broker redelivery after lost ack and Mock PSP recovery. Full 5-minute outage and DLQ redrive remain pending.
