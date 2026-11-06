# Merchant Identity & Authorization — v0.6

**Status: Development-only signed-JWT experiment. CI verified 38/38 tests. NOT a production authentication system.**

This increment replaces the insecure \`X-Demo-Merchant-Id\` identity selection across merchant payment/refund routes with a signed JWT access token. MerchantId is always derived from the validated principal; headers and body cannot change the selected tenant.

## Domain architecture and ownership

- **Merchant Identity / IAM (supporting context, future):** authenticates a merchant client, issues/verifies access token through an approved external OIDC provider and decides authorization grants with merchant lifecycle status.
- **Payment (core):** receives authenticated MerchantId and enforces tenant-owned Payment resource access.
- **Refund:** receives authenticated MerchantId and requires separate refund permission before budget reservation. Stronger finance approval thresholds are pending.
- **Provider Integration:** validates partner-specific HMAC/mTLS/PSP callback identity independently of a merchant JWT. Mock webhook is intentionally \`AllowAnonymous\` for *JWT*, but rejects unsigned HMAC.

## HTTP route policies

| Operation | Required scope | Tenant source | HTTP on denial |
|---|---|---|---|
| POST /v1/payments | payments:create | Validated \`merchant_id\` claim | 401 unauthenticated / 403 insufficient permission |
| GET /v1/payments/{id} | payments:read | Validated JWT + tenant-filtered DB read | 403 insufficient scope / 404 other tenant |
| POST /v1/payments/{id}/refunds | refunds:create | Validated JWT + tenant/payment eligibility | 403 scope / 409 ineligible or other tenant |
| GET /v1/refunds/{id} | refunds:read | Validated JWT + tenant-filtered DB read | 403 scope / 404 other tenant |
| POST /v1/providers/mock/webhook | **not a merchant endpoint** | HMAC over raw payload/timestamp | 401 invalid HMAC |
| GET /health/live | Public liveness | None | 200 |

**Fallback AuthorizationPolicy requires authenticated caller** by default. Only liveness and the HMAC-protected mock PSP callback are explicitly anonymous in JWT middleware. All remaining merchant endpoints must declare a scoped policy.

## Local development JWT details (not production)

- Current scheme: **HS256** with a local dev-only secret \`MerchantAuth:DevSigningKey\` (32+ UTF-8 bytes).
- Valid issuer = \`https://merchant-auth.local.test\`.
- Valid audience = \`paymentgateway.mock.api\`.
- Required: signed token, expected signature algorithm, \`iss\`, \`aud\`, \`exp\`, authenticated \`sub\`, exactly one nonempty GUID \`merchant_id\`, specific \`scope\`.
- JWT lifetime checked with **zero clock skew** in this experiment. Actual issuer/clock-skew policy needs production agreement.
- No JWT minting route is exposed in Gateway API.
- The API still **refuses startup outside Development** because Mock PSP and mock banking workflows are not production safe.

Official bearer validation guidance: https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-jwt-bearer-authentication?view=aspnetcore-10.0

## Local developer runbook

1. Prepare mock-only environment:

~~~bash
cd deploy/compose
cp .env.example .env
# Choose new local-only POSTGRES_PASSWORD, RABBITMQ_PASSWORD,
# MOCK_WEBHOOK_SECRET and MERCHANT_DEV_JWT_KEY
docker compose up --build -d
~~~

2. From the **repository root**, export the same local signing secret as Compose and mint a short-lived synthetic merchant token:

~~~bash
export MERCHANT_DEV_JWT_KEY='your-unique-32-or-more-byte-local-development-signing-key'
export MERCHANT_ID='11111111-1111-4111-8111-111111111111'
TOKEN=$(python3 scripts/security/mint_mock_merchant_jwt.py \
  --merchant "$MERCHANT_ID" \
  --scopes 'payments:create payments:read refunds:create refunds:read')
~~~

This local script is a **test token generator**, not an identity provider; do not put the dev signing key in a merchant/client app or share it.

3. Create a synthetic Payment:

~~~bash
curl -i -X POST http://127.0.0.1:8080/v1/payments \
  -H "Authorization: Bearer $TOKEN" \
  -H 'Idempotency-Key: auth-demo-order-001' \
  -H 'Content-Type: application/json' \
  -d '{"merchantReference":"order-001","amountMinor":100000,"currency":"VND","paymentMethod":"MOCK"}'
~~~

4. Query a Payment:

~~~bash
curl -i "http://127.0.0.1:8080/v1/payments/$PAYMENT_ID" \
  -H "Authorization: Bearer $TOKEN"
~~~

5. Request a refund only after Mock PSP has confirmed Payment \`Succeeded\`, using \`refunds:create\`; try a different signed merchant token against the Payment or Refund ID and expect no cross-tenant access.

**All listening demo ports are loopback-only.** Do not expose this local bearer scheme to public traffic.

## CI verification

[CI run 42158393241 — **38/38 passed**: 3 unit tests and 35 PostgreSQL/RabbitMQ/HTTP integration tests](https://github.com/mic9971/PaymentGateway-Architecture/actions/runs/42158393241).

| ID | Test and expected behavior | CI |
|---|---|---|
| HTTP-AT-01 | Spoofed \`X-Demo-Merchant-Id\` with no signed token → 401 | Passed |
| HTTP-AT-02 | Expired / altered signature / wrong issuer or audience → 401 | Passed |
| HTTP-AT-03 | Valid token missing \`scope\` or \`merchant_id\` → 403 | Passed |
| HTTP-AT-04 | JWT merchant A creates & reads; merchant B GET → 404 despite header spoof | Passed |
| HTTP-AT-05 | Refund action requires \`refunds:create\`; other merchant rejected; read isolation | Passed |
| HTTP-AT-06 | Public health remains 200; unsigned Mock PSP webhook remains 401 | Passed |

These run against actual ASP.NET Core minimal API endpoints using \`WebApplicationFactory\`, PostgreSQL and RabbitMQ services. They do **not** validate a real external OIDC/JWKS provider, provisioning or rate-limits.

## Security and enterprise architecture gaps (OPEN)

1. **Real OIDC provider** with trusted asymmetric JWKS/HTTPS, issuer discovery, key rotation, metadata cache and incident response.
2. **Merchant onboarding/status lifecycle**; \`merchant_id\` signed claim currently has no registry validation, suspension/revocation or authorized merchant account relationship.
3. **Client credentials and authorization issuance**; local mint script controls all scopes, so it is not an access-management service.
4. **Refund step-up/high-value policies**, approval thresholds, four-eyes control, separation of finance roles and adaptive fraud controls.
5. **Key management, token replay risk, mTLS/DPoP as appropriate**, rate limits, token revocation, abuse monitoring and alerting.
6. **Audit trail** proving issuer, \`sub\`, client application, \`merchant_id\`, command permission, correlation ID, request outcome and operator intervention.
7. Load, failover and incident game days; reliable status of tenant identity under IdP outage.
8. A real deployment cannot simply remove the non-Development startup guard; all Mock PSP and mock security infrastructure must be replaced, contracts and operations reviewed.

## Review package

- [Implementation Tracker v0.6 — 33 sheets](https://docs.google.com/spreadsheets/d/1h6KVganNNwQRahJR6ZTwI0HemtXZ1-hr/edit?usp=drivesdk&ouid=116017105754217906634&rtpof=true&sd=true), preserving all 27 previous tabs.
- [ADR-0021](../04-solution-proposal/adrs/ADR-0021-merchant-identity-authorization.md): signed JWT vs dev header, context ownership and production gate.
- [Auth review gates](./AUTH-GATES.csv): explicit open signoff items.
- Feature branch \`feature/merchant-identity-authorization\`, Draft PR #7 → Draft PR #6. **No merges approved.**
