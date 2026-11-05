# 02 — Domain Model: PaymentIntent, PaymentAttempt, Idempotency and Evidence

## Logical aggregate diagram (candidate)

~~~mermaid
classDiagram
    class PaymentIntent {
      +UUID PaymentId
      +UUID MerchantId
      +long AmountMinor
      +string Currency
      +PaymentState State
      +long Version
      +Transition(evidence)
    }
    class PaymentAttempt {
      +UUID AttemptId
      +UUID PaymentId
      +string ProviderId
      +string ProviderKey
      +AttemptState State
      +int ClaimGeneration
      +datetime LeaseUntil
    }
    class IdempotencyRecord {
      +UUID MerchantId
      +string Operation
      +string Key
      +byte[] RequestHash
      +UUID PaymentId
    }
    class PaymentEvidence {
      +UUID EvidenceId
      +UUID AttemptId
      +string ProofType
      +string ProviderReference
      +datetime ReceivedAt
    }
    class PaymentStateHistory {
      +UUID HistoryId
      +UUID PaymentId
      +PaymentState From
      +PaymentState To
      +string EvidenceRef
    }
    PaymentIntent "1" --> "0..*" PaymentAttempt : attempted through PSP
    PaymentIntent "1" --> "1" IdempotencyRecord : logical create request
    PaymentIntent "1" --> "0..*" PaymentEvidence : verified outcomes
    PaymentIntent "1" --> "0..*" PaymentStateHistory : audit
~~~

Diagram is a conceptual relationship, not a decision that all nodes share one .NET aggregate/transaction. **PaymentAttempt as independent aggregate root vs child entity is intentionally under ADR-0014**. Provider correlation may live behind Provider Integration ACL, not Payment's private table.

## Aggregate responsibilities

| Candidate aggregate | Owns | Boundary invariant | Transaction behavior |
|---|---|---|---|
| PaymentIntent | Merchant, amount, currency, lifecycle, state version | No invalid transitions or false Succeeded | Short conditional state update + evidence + outbox |
| PaymentAttempt | One PSP invocation, provider key, claim generation | No unauthorized repeat of financial side effect | Claim, call PSP *outside TX*, persist outcome |
| IdempotencyRecord | Merchant/op/key, canonical request hash, result locator | One logical operation per key and payload | Unique DB claim in same TX as logical Payment |
| PaymentEvidence | Proof reference, provider identity, source timestamp, hash | No unauthenticated / unrelated proof can finalize | Append verified evidence with state change |
| StateHistory | Transition provenance and actor | No silent privileged status rewrite | Append in same TX as transition |

## Identity and amount decisions

- PaymentId: gateway-generated opaque UUID (not a merchant-generated primary key).
- MerchantId: from authenticated context.
- MerchantReference: merchant-facing order identifier; uniqueness rules **TBD** (may vary by merchant/operation).
- Amount: positive integer \`amount_minor\` plus ISO currency code; **currency minor-unit rules and PSP conversion must be confirmed**, not assumed for all currencies.
- ProviderReference: scoped by provider **and possibly PSP merchant account**; never assume globally unique.
- Idempotency: uniqueness key \`merchant_id + operation + idempotency_key\`, request fingerprint checked on repeat.

## Financial truth

A generated QR code, accepted HTTP request or PSP's generic "success" flag may represent only *instruction creation*. Payment only enters Succeeded after the configured business-finality rule identifies verifiable PSP/bank evidence.

## No microservices assumption

All logical contexts may share a PostgreSQL deployment under explicit write ownership. DDD is about language, boundaries and invariants, **not count of independent microservices**.
