# Ubiquitous Language and Translation Rules

Bounded Contexts exist because the same word can have different precise meanings across Payment, PSP, Merchant and Finance. Maintain a **context-scoped glossary**, not a single universal status enum copied from PSPs.

| Term | Context | Meaning | Never confuse with |
|---|---|---|---|
| PaymentIntent | Payment | Logical merchant payment initiation | Provider charge or settlement |
| PaymentAttempt | Payment | One invocation attempt correlated to a PSP | Logical PaymentIntent |
| Pending | Payment | Known unfinished step/customer action | Paid or settled |
| Unknown | Payment | Financial side-effect outcome not determined | Failed or safe to retry |
| Succeeded | Payment | Verified success at agreed business finality | QR issued/API accepted |
| ProviderReference | Provider Integration | External identifier scoped to a PSP and operation | Global internal PaymentId |
| QRReady | Provider Integration / VietQR | QR/payment instruction is available | Payer transferred money |
| LateTransfer | Provider Integration / Finance | Funds evidence arrives after order expiry | Automatically refundable without policy |
| EligibleProvider | Routing | PSP meets merchant, method, amount and availability policy | Safe to failover after ambiguous charge |
| RefundRequested | Refund | Return-money operation was requested | PSP already refunded |
| RefundBudget | Refund/Payment decision pending | Verified refundable limit and reserved/completed totals | Cached Payment amount |
| Reconciled | Reconciliation | Gateway and independent PSP/bank records matched | Merchant notified |
| Discrepancy | Reconciliation | Evidence differs or is missing; investigation required | PSP declined |
| MerchantWebhook | Notifications | Gateway notification to merchant | Inbound PSP callback |
| Settlement | Finance/external PSP | Contractually defined final transfer/clearing | Gateway Payment.Succeeded by default |
| AuditEvidence | Operations | Provenance of changes and operator action | Financial ledger |
| Idempotency | Payment / PSP contract | Repeated logical operation yields same intended result | Guaranteed exactly-once financial effect |
| OutboxMessage | Infrastructure | Durable publication intent committed with business data | A business bounded context by itself |

## Status translation example

Different providers may return \`approved\`, \`accepted\`, \`pending\`, \`completed\` or \`success\`. Provider Integration must map these **based on the signed PSP contract**, not on lexical similarity:

- **Request accepted / QR generated:** gateway may become Pending/RequiresAction.
- **Definitive confirmed financial outcome:** gateway may transition to Succeeded.
- **Definitive decline with no side effect:** gateway may transition to Failed.
- **Timeout / disconnected / unknown provider state:** gateway remains Unknown.
- **Final statement / settlement:** separate Finance concept, may be later than payment success.

## Domain language verification

Workshop with merchant checkout, provider team and Finance. Every term should have an owner, sample payload/statement, business consequence, status and date of confirmation. The Excel \`DDD Language\` sheet tracks the 18 initial terms.

When a provider adds a new status, update the adapter ACL and add a contract test; do not inject PSP-specific status into Payment's state machine without architecture review.
