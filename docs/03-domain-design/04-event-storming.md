# Event Storming — Payment Gateway Happy Path & Failure Path

**Context:** Orchestration Gateway without confirmed custody. This is a **discovery exercise**, not signed financial process.

## Event-storming notation

- **Command**: user/operator/worker asks system to do something.
- **Domain Event**: business fact that happened inside the system.
- **Policy**: decision/rule triggered by event.
- **External PSP evidence**: third-party fact; must be authenticated, interpreted and correlated.
- **Read Model**: projection used by UI/reconciliation, not necessarily authority for money.
- **Hotspot**: ambiguity/decision to validate with Product, Finance or PSP.

## Happy path — pay by QR / wallet

~~~mermaid
flowchart LR
    A["Merchant: SubmitPayment"] --> B["PaymentCreated"]
    B --> C["Routing: ProviderSelected"]
    C --> D["Provider: QR/CheckoutInstructionCreated"]
    D --> E["Payer completes bank/wallet action"]
    E --> F["PSP: signed callback / inquiry"]
    F --> G{"Authenticated, correlated and definitive?"}
    G -->|Yes| H["PaymentSucceeded"]
    G -->|No/unknown| I["PaymentUnknown / investigate"]
    H --> J["MerchantWebhookRequested"]
    J --> K["Merchant notified (at least once)"]
~~~

**QR generated != payment succeeded.** Different PSPs may send preliminary events, duplicate final events or no event at all. Define PSP-specific state mapping.

| Step | Command / actor | Event or observed outcome | Owner | Rule / evidence |
|---|---|---|---|---|
| 1 | Merchant sends CreatePayment + stable key | PaymentRequested | Payment | Unique merchant/op/idempotency scope |
| 2 | Payment selects PSP | ProviderSelected | Routing | Eligibility, merchant policy and routing version recorded |
| 3 | Worker calls adapter | AttemptStarted | Payment | Durable attempt claim, stable provider key if supported |
| 4 | PSP returns QR/redirect | AwaitingPayerAction | Provider ACL → Payment | No financial success yet |
| 5 | PSP confirms / bank evidence arrives | ProviderOutcomeObserved | Provider ACL | Verify PSP signature, reference, amount, merchant |
| 6 | Payment validates transition | PaymentSucceeded | Payment | Only definitive evidence can finalize |
| 7 | Outbox publishes to RabbitMQ | PaymentStatusChanged | Payment | Same transaction as state transition |
| 8 | Delivery worker posts merchant webhook | MerchantWebhookDelivered | Notifications | Signed event ID, retry and delivery record |
| 9 | Finance compares PSP statement | ReconciliationMatched | Reconciliation | Independent evidence and audit |

## Hotspot A — network timeout after PSP side effect

~~~mermaid
flowchart TD
    A["PaymentAttempt invoked PSP"] --> B["HTTP timeout / lost response"]
    B --> C["PaymentOutcomeUnknown"]
    C --> D{"PSP status inquiry supported?"}
    D -->|Yes| E["Query with stable provider ref"]
    D -->|No| F["Wait for verified statement / investigation"]
    E --> G{"Verified status"}
    G -->|Paid| H["PaymentSucceeded"]
    G -->|Definitively failed| I["PaymentFailed"]
    G -->|Still unknown| F
    F --> J["No blind cross-PSP retry"]
~~~

Questions: Does PSP guarantee idempotent create? Are references stable? Does a timeout hide an accepted charge? Can QR transfer arrive late after expiry?

## Hotspot B — two refunds simultaneously

- Refund command A asks to refund 70% and command B asks to refund 60%.
- Both read the same Payment amount. Naive independent validation would authorize 130%.
- Require **atomic reservation** in Refund (or a Payment-owned reservation contract); only one request may pass if sum exceeds the verified limit.
- PSP refund may itself time out; reservation and UNKNOWN are not released merely because no response arrived.

## Hotspot C — payment and reconciliation disagree

PSP statement says Paid; Payment DB says Unknown. Reconciliation records mismatch with proof, sends a controlled command to Payment; Payment validates evidence and transition; it emits a new auditable business event. Reconciliation never writes Payment-owned tables directly.

## Event catalog discipline

- External PSP callback is **not** automatically a trusted internal domain event.
- Prefer event naming as past tense fact: \`PaymentSucceeded\` vs command \`CreatePayment\`.
- Include \`event_id\`, schema version, correlation IDs, merchant/payment references, and minimal safe payload.
- Out-of-order delivery and duplicate receipt must have defined behavior.
- An operator replay is a **new controlled action with evidence**, not a silent re-run of PSP side effects.

## Next workshop

Validate these three hotspots with **Product, PSP Lead and Finance**. Translate accepted policies into State Machine rules, ADR, invariants, database constraints and executable failure tests.
