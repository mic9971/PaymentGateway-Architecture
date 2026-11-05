# 07 — HTTP, Provider and Event Contracts (Candidates)

## HTTP endpoints

| Method | Path | Contract | Status |
|---|---|---|---|
| POST | /v1/payments | Merchant-scoped payment + idempotency key, returns ID and pending/next action | Proposed |
| GET | /v1/payments/{paymentId} | Merchant-scoped status lookup | Proposed |
| POST | /v1/payments/{paymentId}/refunds | Refund idempotency, amount and reason | Proposed |
| GET | /v1/refunds/{refundId} | Merchant-scoped refund status | Proposed |
| POST | /v1/providers/{provider}/webhook | PSP signature + replay verification | PSP-specific TBD |
| GET | /v1/health/live | Process liveness, not database-dependent | Proposed |
| GET | /v1/health/ready | Dependencies readiness for traffic | Proposed |

## Example API request (illustrative, no actual payment captured)

~~~json
{
  "merchant_reference": "order-demo-101",
  "amount_minor": 500000,
  "currency": "VND",
  "payment_method": "VietQR",
  "customer_action_return_url": "https://merchant.example/checkout/return"
}
~~~

HTTP header: Idempotency-Key: one stable key per logical payment initiation. Request amount uses integer **minor units** per approved currency convention, not float money.

~~~json
{
  "payment_id": "example-uuid",
  "status": "PENDING",
  "next_action": {
    "type": "QR_DISPLAY",
    "url": "https://provider.example/example-qr"
  }
}
~~~

This response proves only instruction/QR availability, NOT funds received. Sensitive callback URLs should be allowlisted and validated; example domains are placeholders.

## Provider Adapter interface concept

The adapter normalizes provider output into:
- Provider reference, attempt correlation, customer next action and authorization status.
- Definitive success vs definitive decline vs **ambiguous UNKNOWN**.
- Whether provider-side idempotency exists and its supported scope/retention.
- Inquiry-by-reference, refund semantics, webhook signature and replay requirements.

Do NOT assume the same PSP retry, QR expiry, or refund guarantees across providers.

## Versioned event envelope

~~~json
{
  "event_id": "uuid",
  "event_type": "PaymentSucceeded",
  "version": 1,
  "occurred_at": "2026-11-05T02:00:00Z",
  "correlation_id": "uuid",
  "merchant_id": "uuid",
  "payment_id": "uuid"
}
~~~

Events are **at least once**; consumers dedup by event_id + consumer name. Avoid sensitive payload when a reference can be used instead. Define schema compatibility before independent deployment.

## Consumer contract tests

- Idempotency duplicate same body / differing body.
- Cross-tenant resource access.
- PSP webhook forged, replayed, delayed or out of order.
- Contract version forward/backward compatibility.
- External PSP fallback only when safe side effects are proven.
