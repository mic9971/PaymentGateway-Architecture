# 04 — Use Cases, HTTP API and Application Boundaries

## Candidate HTTP endpoints

| Method | URL | Identity / idempotency | Response outline |
|---|---|---|---|
| POST | /v1/payments | Authenticated merchant + required Idempotency-Key | 202 Accepted / PaymentId, status, next action |
| GET | /v1/payments/{paymentId} | Merchant owns resource | 200 Payment read model |
| GET | /v1/payments/{paymentId}/attempts | Operator/privileged policy; not default merchant API | Sanitized attempt history |
| POST | /v1/providers/{providerId}/webhooks | PSP-specific verified signature | Ack after durable receipt per PSP contract |

The status code 202 is an **API proposal** for asynchronous creation, not universally required. If initial processing returns a QR/redirect immediately the response model must support that state. Repeated Idempotency-Key with same canonical payload should return the same operation identity; different payload returns a documented conflict.

## CreatePayment request

~~~http
POST /v1/payments HTTP/1.1
Authorization: Bearer <merchant-access-token>
Idempotency-Key: merchant-generated-unique-key
Content-Type: application/json
~~~

~~~json
{
  "merchant_reference": "order-example-101",
  "amount_minor": 500000,
  "currency": "VND",
  "payment_method": "VIETQR",
  "return_url": "https://merchant.example/checkout/return"
}
~~~

The example is **not** production-ready: allowed redirect origins, currency exponent, request limits, merchant references, settlement policy and PSP routing must be approved. Never trust an arbitrary \`merchant_id\` supplied in the JSON body; derive merchant identity from verified authentication context.

## Candidate response

~~~json
{
  "payment_id": "00000000-0000-0000-0000-000000000001",
  "status": "PENDING",
  "next_action": null
}
~~~

\`next_action\` can later contain a short-lived PSP QR/redirect instruction. Its existence does **not** prove funds received. Do not expose raw PSP credentials, cardholder data, internal exception stacks or high-risk payment verification secrets.

## Application use-case boundaries

1. **CreatePaymentHandler:** validate merchant and monetary rules; delegate an atomic idempotent operation to DB transaction boundary; return stable logical result.
2. **GetPaymentHandler:** tenant-scoped query and read-model projection only.
3. **DispatchAttemptHandler:** durable attempt claim before provider call; no PostgreSQL transaction across network.
4. **ApplyVerifiedOutcomeHandler:** checks authority, correlation and allowed transition; commits evidence, state, audit, outbox.
5. **ResolveUnknownHandler:** accepts verified PSP inquiry or Finance-approved statement resolution; never assumes timeout means no charge.

## Error classification

- 400/422: invalid request or unsupported payment method.
- 401/403: authentication/authorization denial.
- 409: same Idempotency-Key with conflicting payload (candidate; confirm policy).
- 429: rate limiting.
- 5xx/timeout: infrastructure ambiguity, **not proof payment failed**.
- PSP business decline: must be normalized based on actual contract, independent of HTTP status.

**Public contract compatibility** and idempotency retention belong in Architecture Review; do not publish raw provider enums to merchants.
