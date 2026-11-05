# API and Event Contracts — Draft

HTTP candidates:
- POST /v1/payments (Idempotency-Key)
- GET /v1/payments/{paymentId}
- POST /v1/payments/{paymentId}/refunds
- POST /v1/providers/{provider}/webhooks

Event candidates:
- PaymentRequested
- PaymentSucceeded
- PaymentFailed
- RefundRequested
- MerchantWebhookDeliveryRequested

Define versioning, correlation ID, tenant identity, secure webhook verification, duplicate delivery semantics and payload retention before implementation.
