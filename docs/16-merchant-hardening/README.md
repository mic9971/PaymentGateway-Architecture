# Merchant Registry, OIDC Candidate & Security Audit — v0.7

**Status: Draft security architecture increment, Mock-only. Production identity, financial controls and external IdP integration are NOT approved.**

## Problem statement

JWT signature validation alone does NOT prove that the named merchant is onboarded, currently active, or entitled to the particular operation. Valid, unexpired JWTs may remain usable after a merchant is suspended unless the resource server enforces a live status/permission check.

This increment supplements the existing JWT bearer validation with PostgreSQL-bound merchant registration and grants. It also introduces an **opt-in OIDC/JWKS configuration candidate**, not a connected identity provider. Full app startup remains Development-only because the Payment/Refund workers interact with Mock PSPs.

## Architectural context map

- **IAM / Merchant Registry (supporting bounded context):** owns `iam.merchants`, `iam.merchant_clients`, active/suspended/closed lifecycle and allowed operation scopes.
- **Payment (core):** trusts only a principal that passed issuer/signature/lifetime validation AND the live IAM registry grant check. Tenant ownership still enforced in SQL.
- **Refund (supporting/core financial capability):** same tenant binding; refund requests additionally require `refunds:create`, but high-value approval/dual-control is NOT implemented.
- **Provider Integration (anti-corruption layer):** Mock PSP callbacks use HMAC independent of merchant JWT.
- **Security Audit (technical):** HTTP access telemetry in `iam.access_audit` is best-effort, not tamper-proof or atomically committed with Payment/Refund.

## HTTP authorization flow

~~~mermaid
flowchart TD
  A["Bearer token"] --> B{"JWT signature, issuer, audience, time valid?"}
  B -->|No| C["401"]
  B -->|Yes| D{"merchant_id + sub + client_id present?"}
  D -->|No| E["403"]
  D -->|Yes| F{"JWT contains required operation scope?"}
  F -->|No| E
  F -->|Yes| G{"IAM merchant Active, client enabled, DB grant present?"}
  G -->|No| E
  G -->|Yes| H["Payment/Refund tenant-filtered application command"]
  H --> I["HTTP access audit (best effort)"]
~~~

Every scoped merchant route checks live registry. No caching was added; **latency and capacity impact need measurement**. The deny-by-default fallback prevents accidental creation of new authenticated-but-unscoped routes.

## Supported local modes

**DevelopmentHmac (default):** local HS256 key, issuer `https://merchant-auth.local.test` and audience `paymentgateway.mock.api`. Local mint script now includes `sub="merchant-<UUID hex>"` and `client_id="local-demo-client"` to match registry. This is not a production token issuer.

**Oidc (configuration candidate):** set `MerchantAuth:Mode=Oidc` plus HTTPS `Authority`, trusted `Issuer`, and expected `Audience`. ASP.NET Core JwtBearer is configured for HTTPS discovery and RS256 JWKS signatures; it still **refuses to run outside Development**. It has not been exercised with a real identity provider or live JWKS. Real provider claim mapping, `client_id`/authorized-party semantics, rotation, outage and metadata validation must be tested and approved before adoption.

Vendor references:
- https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-jwt-bearer-authentication?view=aspnetcore-10.0
- https://learn.microsoft.com/en-us/aspnet/core/security/authorization/policies?view=aspnetcore-10.0

## Development runbook

1. From `deploy/compose`, start the existing local mock stack using the v0.6 instructions. For **existing** PostgreSQL volumes, apply the new migration:

~~~bash
docker compose exec -T postgres psql -U payment_demo -d payment_demo \
  < postgres-init/006_merchant_registry_audit.sql
~~~

2. Register a **synthetic** merchant through local SQL only. There is intentionally no anonymous registration or admin API:

~~~sql
INSERT INTO iam.merchants(merchant_id,status)
VALUES ('11111111-1111-4111-8111-111111111111','Active')
ON CONFLICT(merchant_id) DO UPDATE SET status='Active';

INSERT INTO iam.merchant_clients
  (merchant_id,issuer,subject,client_id,granted_scopes,enabled)
VALUES ('11111111-1111-4111-8111-111111111111',
        'https://merchant-auth.local.test',
        'merchant-11111111111141118111111111111111',
        'local-demo-client',
        ARRAY['payments:create','payments:read','refunds:create','refunds:read'],
        true);
~~~

3. Export `MERCHANT_DEV_JWT_KEY` in your local shell with the **same local-only value** configured in Docker Compose. Mint a short-lived token from the repository root:

~~~bash
python3 scripts/security/mint_mock_merchant_jwt.py \
  --merchant 11111111-1111-4111-8111-111111111111 \
  --scopes 'payments:create payments:read refunds:create refunds:read'
~~~

The script only mints a local demo token; client registration and grants must already exist in the database.

4. Create a Mock payment using `Authorization: Bearer $TOKEN`. Without a matching registration/grant expect 403; no signed token gives 401.

5. Suspend the merchant with a local SQL update, then reuse the **same unexpired token**:

~~~sql
UPDATE iam.merchants SET status='Suspended',updated_at=now()
WHERE merchant_id='11111111-1111-4111-8111-111111111111';
~~~

Expected: 403 for a new payment/refund request, even though the JWT signature and `exp` remain valid. Re-enable only for approved local testing.

6. Inspect authorization telemetry:

~~~sql
SELECT timestamp_utc,request_id,merchant_id,subject,client_id,
       method,request_path,http_status
FROM iam.access_audit ORDER BY timestamp_utc DESC LIMIT 30;
~~~

No raw bearer token is stored in this table; audit insertion failures are logged and **do not roll back a completed financial API transaction**.

## CI and acceptance

[Verified CI: 42/42 tests passed (3 unit + 39 integration)](https://github.com/mic9971/PaymentGateway-Architecture/actions/runs/42160759634). New integration tests: `IAM-T07` merchant suspension, `IAM-T08` DB grant intersection, `IAM-T09` unregistered merchant denied, `IAM-T10` HTTP audit of a successful Mock payment.

Also fixed a previously exposed NpgsqlDataReader disposal bug in an empty Payment Worker claim path and scoped that integration test by PaymentId instead of selecting unrelated pending jobs. Test success status is based on the linked GitHub Actions run, not on document publication.

## Risk and review gates

- No externally connected OIDC issuer, tenant onboarding, client-credential grant or key rotation test.
- No production-grade token revocation, JWT replay prevention, secrets management, TLS/mTLS topology, or IAM high availability.
- No trustworthy financial audit trail with retention/immutable integrity guarantees; current HTTP audit is operational telemetry only.
- No merchant dispute/billing/KYC states beyond Active, Suspended, Closed; suspending a merchant during an in-flight payment does not reverse provider effects.
- No compliance review, real PSP integrations, high-value refund dual control, load and disaster recovery game days.
- Registry lookup every merchant request adds DB dependency; measure saturation and choose bounded safe caching/invalidations before production.
- **Keep all PRs Draft** and retain Development-only startup guard.

## Artifacts

- [ADR-0022 — Merchant registry and optional OIDC](../04-solution-proposal/adrs/ADR-0022-merchant-registry-oidc.md)
- [IAM security gates](./IAM-GATES.csv)
- [Implementation Tracker v0.7 — 39 sheets](https://docs.google.com/spreadsheets/d/13JwYvpAZ4uXQlO6usEmFBFJj44VcyEZw/edit?usp=drivesdk&ouid=116017105754217906634&rtpof=true&sd=true): the 33 prior sheets plus six new IAM review sheets.
