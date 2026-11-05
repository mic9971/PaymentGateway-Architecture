# 03 — Payment State Machine and Contract Semantics

**Proposed states:** Created → Pending → Processing → RequiresAction → Succeeded / Failed / Unknown / ExpiredPendingReview / Investigation.

Do **not** map PSP's raw enum directly onto the Payment domain enum. Each PSP adapter must define what its status proves.

~~~mermaid
stateDiagram-v2
    [*] --> Created
    Created --> Pending : Internal DB commit
    Pending --> Processing : Durable worker attempt claim
    Processing --> RequiresAction : QR/redirect available
    Processing --> Succeeded : Verified financial success
    Processing --> Failed : Definitive no-charge decline
    Processing --> Unknown : Timeout/ambiguous response
    RequiresAction --> Succeeded : Verified paid webhook/inquiry
    RequiresAction --> Unknown : Unable to determine outcome
    RequiresAction --> ExpiredPendingReview : Expiry with no finality evidence
    Unknown --> Succeeded : Inquiry / recon verifies paid
    Unknown --> Failed : PSP proves no financial effect
    Unknown --> Investigation : No authoritative inquiry
    ExpiredPendingReview --> Investigation : Late transfer, disputed evidence
    Failed --> Investigation : Contradictory late paid evidence
~~~

## Transition guard table

| From | Trigger | To | Guard and evidence | Test |
|---|---|---|---|---|
| Created | Internal DB commit | Pending | Valid merchant + unique idempotent operation | PC-T01 |
| Pending | Worker claim | Processing | Atomic version/lease claim | PC-T03 |
| Processing | QR/redirect created | RequiresAction | *Provider instruction*, not money received | PC-T19 |
| Processing/RequiresAction | Verified paid result | Succeeded | Signed/correlated bank/PSP evidence + business finality | PC-T06 |
| Processing | Definitive no-effect decline | Failed | Contract proves charge did not occur | PC-T05 |
| Processing | PSP request timeout | Unknown | Side effect cannot be ruled out | PC-T04 |
| Unknown | Authoritative inquiry paid | Succeeded | Stable reference and proof | PC-T08 |
| Unknown | Inquiry proves no effect | Failed | Contractually definitive | PC-T08 |
| RequiresAction | QR expires | ExpiredPendingReview | Late-transfer and expiry rules approved | PC-T14 |
| Succeeded | Stale pending/failed callback | **No change** | State regression forbidden; audit event | PC-T09 |
| Failed or ExpiredPendingReview | New paid evidence | Investigation | Do not silently discard evidence | PC-T14 |

**Terminality is not yet approved.** Whether Failed, Succeeded, ExpiredPendingReview, Cancellation or ChargedBack are terminal must be defined by Finance/Product/PSP. Refund has a **separate** lifecycle and is not a simple reverse arrow from Succeeded.

## Concurrency strategy

- Every transition includes \`WHERE version = expected_version AND state IN allowed_predecessor_states\`; if zero rows update, reload/verify instead of blindly reapplying.
- The evidence record, state update, audit and outbox event must commit together where they represent one internal transition.
- Late events may require investigation; their arrival sequence is not an absolute truth order.
- A worker "lease expired" flag proves nothing about whether PSP charged.
- Webhook callback is a **reported external fact**, not automatically trusted; signature/auth, provider reference, amount, currency and merchant correlation must pass first.

**Exit requirement:** PSP status matrix and financial finality rules signed off.
