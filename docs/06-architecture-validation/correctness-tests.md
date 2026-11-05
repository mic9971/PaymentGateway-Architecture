# Correctness Tests — Proposed Cases

1. Concurrent identical requests, one idempotency record and consistent response.
2. HTTP timeout after provider succeeds: Unknown + inquiry; no unsafe duplicate charge.
3. Outbox publisher crash before/after publish: no lost committed event; duplicates safe.
4. RabbitMQ redelivery: consumer idempotent and transaction atomicity verified.
5. PSP webhook forgery/replay/order inversion: verified, deduplicated, state safe.
6. Refund concurrency: total successful refunds never exceed permitted amount.
7. Cross-merchant access denied and audited.

Test real provider semantics in sandbox; mocks alone are insufficient.
