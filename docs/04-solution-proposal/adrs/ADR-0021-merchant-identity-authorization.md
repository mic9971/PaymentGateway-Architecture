# ADR-0021 — Merchant Authentication and Resource Authorization Boundaries

- **Status:** Proposed, not production accepted.
- **Date:** 2026-11-06.
- **Decision owners:** Security/IAM Lead, Solution Architect, Product, Finance.
- **Affected contexts:** Merchant Identity, Payment, Refund, Provider Integration.

## Problem

The original local Mock API accepted \`X-Demo-Merchant-Id\`, letting a caller choose any MerchantId. A multi-merchant payment system cannot trust a client-controlled tenant identifier, especially for Payment and Refund financial operations.

## Alternatives

A. **Client-supplied MerchantId header:** Rejected; unauthenticated impersonation and IDOR risk.

B. **Signed JWT access token with merchant claim and fine-grained scopes:** Implemented as a local Development proof. The Gateway verifies signature, expected issuer/audience/expiry and extracts a validated, single merchant claim. Explicit scope policies apply to payment and refund actions.

C. **External OIDC/OAuth2 Identity Provider + asymmetric JWKS token validation:** **Target production candidate** pending provider, issuer, tenant mapping, credential lifecycle, onboarding/suspension and security approval. Not implemented.

## Experimental decision

B only for mock-only Development. The API refuses to start outside Development. No anonymous minting endpoint is added. Merchant JWT authentication **does not** authorize PSP callbacks; those use their own Mock PSP HMAC scheme.

## Context ownership and separation

- Merchant Identity owns credentials, merchant-to-principal link, grants, suspension and token issuance with an external IdP.
- Payment/Refund consume immutable validated authentication context and enforce resource ownership in their data access and command boundaries.
- Provider Integration verifies each PSP callback under provider contract; merchant JWT cannot stand in for PSP authenticity.
- Refund authorizations may need dual control/step-up beyond a single token scope.

## Risks and validation evidence

[38/38 CI tests](https://github.com/mic9971/PaymentGateway-Architecture/actions/runs/42158393241) validate mock auth via HTTP TestServer, scope denial and tenant isolation. This is **not** proof of production IdP configuration, key rotation, credential storage, account onboarding, audit, anti-abuse or high-value refund approval.

## Required production gates

1. Select supported Identity Provider and document OIDC issuer/audience, JWKS/rotation and token type.
2. Define merchant status, principal/client registration, termination and emergency revocation.
3. Approve payment/refund scope vocabulary and high-risk step-up/dual authorization.
4. Implement real HTTPS trust boundary and protected secret/credential storage.
5. Audit token claim provenance and every financially sensitive command.
6. Run JWT replay, client isolation, idp outage, revocation and key-rotation tests.
7. Remove Mock PSP/Development-only infrastructure before any real-money deployment.

**No business approval or PR merge is implied by this proof-of-concept.**
