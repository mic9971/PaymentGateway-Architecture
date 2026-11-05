# 06 — PSP Side Effects, Partial Failure and Safe Recovery

Payment Gateway correctness is not complete until **external PSP side effects** are considered. PostgreSQL transactions do not roll back charges at third-party PSPs.

## The critical incident

1. Worker durably claims PaymentAttempt A.
2. Worker sends CreatePayment/Charge to PSP.
3. PSP processes the charge successfully.
4. Network loses PSP response or worker crashes before saving outcome.
5. RabbitMQ redelivers PaymentRequested.

**Wrong:** Worker assumes no response = failure and issues a fresh PSP charge or switches to PSP B.  
**Safe candidate:** Persist/recognize Unknown, keep stable PSP operation reference, perform status inquiry/reconciliation, only retry with a verified provider-side idempotency guarantee or definitive no-effect proof.

## Failure classification decision tree

~~~mermaid
flowchart TD
  A["PSP request / callback outcome"] --> B{"Definitive, authenticated proof?"}
  B -->|Paid under business finality| S["Payment Succeeded"]
  B -->|Definitive no-effect decline| F["Payment Failed"]
  B -->|No / ambiguous| U["Payment Unknown"]
  U --> Q{"PSP supports status inquiry by stable reference?"}
  Q -->|Yes| I["Query status"]
  Q -->|No| E["Escalate to Finance + statement reconciliation"]
  I --> R{"Authoritative response?"}
  R -->|Paid| S
  R -->|Definitive no financial effect| F
  R -->|Still unknown| E
  E --> H["Hold Unknown, preserve evidence, investigate"]
~~~

## Why three idempotency layers matter

| Layer | What it protects | What it **does not** guarantee |
|---|---|---|
| HTTP Idempotency-Key + PostgreSQL unique claim | Duplicate **logical** create operation from merchant | Provider didn't charge twice |
| Worker claim / Inbox | Duplicate local processing after RabbitMQ redelivery | PSP didn't execute before worker crashed |
| PSP idempotency key / inquiry | Safe reuse/verification of remote financial operation **if PSP contract supports it** | Universal exactly-once across all providers |

The third layer is a **contract dependency**. If PSP lacks idempotent create and status inquiry, block blind automated recovery and require Finance/PSP-approved manual/evidence-driven procedures; do not silently promise a deterministic resolution time.

## Provider capability matrix required before implementation

| Capability | PSP A | PSP B | Gate |
|---|---|---|---|
| Create/payment instruction semantics | TBD | TBD | PSP contract |
| Provider idempotency key scope and retention | TBD | TBD | PSP contract + sandbox |
| Query status using stable reference | TBD | TBD | PSP contract + sandbox |
| Webhook signature / timestamp / replay window | TBD | TBD | Security + PSP |
| Paid vs Authorized vs Settled meaning | TBD | TBD | Product + Finance |
| Refund idempotency and inquiry | TBD | TBD | Finance + PSP |
| QR expiry, late transfer and duplicate transfer rules | TBD | TBD | VietQR/bank contract |

## Evidence for each PSP attempt

- PaymentId and AttemptId (gateway-generated).
- PSP identity + PSP merchant account and stable provider operation key.
- Request fingerprint, timestamp, provider correlation references and error class.
- Authenticated webhook/inquiry/statement evidence references, not raw sensitive payloads unless legally necessary.
- Observed result kind: \`ActionRequired / DefinitiveSuccess / DefinitiveFailure / Ambiguous\`.
- Any explicit operator/Finance case and approved resolution.

## Retry policy questions

- What proves no request reached the PSP?
- What does the PSP do on repeating the same provider-side idempotency key after the first succeeded?
- What happens after PSP's idempotency retention window expires?
- Does querying status guarantee authoritative outcome or return eventually consistent information?
- Can a charge complete after the checkout/QR expires?
- Are provider routing and payout/settlement policies tied to a specific PSP account?

No answer should be invented from generic HTTP codes. **A 500 or timeout can still follow a successful financial side effect.**

## Test exit gate

Run at least: PC-T04 charged-then-timeout, PC-T07 callback-before-response, PC-T08 missing callback but paid inquiry, PC-T09 spoof/replay, PC-T14 late QR transfer, PC-T19 HTTP-200-but-only-QR.

Do not approve a production PSP integration before a sandbox and contract reviewer provide evidence.
