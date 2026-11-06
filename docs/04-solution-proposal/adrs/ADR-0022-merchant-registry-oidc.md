# ADR-0022 — Merchant Registry Authorization and External OIDC Validation Candidate

- **Status:** Proposed; neither security nor architecture approval recorded.
- **Date:** 2026-11-06.
- **Owners:** Security/IAM Lead, Solution Architect, Product, Finance.
- **Related:** ADR-0021 Merchant Identity, ADR-0019 Provider Webhooks, ADR-0020 RefundBudget.

## Context

A cryptographically signed JWT may outlive a merchant's authorization. Relying on the \`merchant_id\` and scope strings inside a token alone does not validate tenant enrollment, client binding or an emergency suspension.

## Alternatives

**A. Trust signed token claims alone until expiry** — rejected as adequate financial authorization for this system; merchant suspension takes effect only at expiry without separate controls.

**B. Dynamic registry check (experimental implementation)** — every merchant operation must have a signed token with \`merchant_id\`, \`sub\`, \`iss\`, \`client_id\`, a matching JWT operation scope, and an active merchant/enabled registered client with matching persisted scope grant. PostgreSQL is consulted by authorization handler. Fail closed on missing registry access.

**C. Centralized authorization policy service (candidate)** — can decouple registry from Gateway and offer distributed policy decisions; requires fault isolation, audit, caching/invalidations and consistency contracts.

## Proposed decision

Experiment with B for a **Mock-only** API. Deny unregistered/suspended clients even when their JWT is otherwise valid. Keep existing tenant-scoped SQL reads/writes as an additional defense layer. Do not claim this is a production-ready IAM or real PSP solution.

Allow separate \`MerchantAuth:Mode=Oidc\` configuration to explore HTTPS JWKS metadata and RS256 validation. No real issuer has been wired or tested; runtime remains Development-only. Specific IdP capabilities and claims must drive final selection.

## Trade-offs

- Registry adds an online PostgreSQL dependency to each authorized request. Test latency, availability and saturation before production.
- Merchant/client grants require an admin onboarding and revocation workflow. No such API is implemented; CI/test SQL seeds only.
- JWT provider metadata/JWKS, key rotation and outage safety are unverified.
- HTTP access audit is best-effort. It is **not** a financial audit transaction, immutable ledger or certified evidence.
- High-value refunds need an accountable two-person authorization mechanism; a single \`refunds:create\` scope is insufficient.

## Required evidence before acceptance

1. Real IdP/JWKS issuer/audience/algorithm and \`sub\`/\`client_id\` mapping test with rotating keys.
2. Product approval for onboarding, suspension, reactivation and merchant status SLO.
3. Security approval for trusted proxies, HTTPS, key handling, incident revocation and abuse control.
4. Finance approval for high-value refund authorization and dual control.
5. Operations proves registry fail-closed, load, database failover and recovery.
6. Independent immutable security/financial audit design with evidence retention.
7. QA confirms mock tests plus real IdP sandbox and API contract checks.

**No production release or PR merge is implied.**
