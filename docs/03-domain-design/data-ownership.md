# Data Ownership — Draft

| Bounded context | Data owned | Source of truth |
|---|---|---|
| Merchant | Merchant settings and credentials metadata | Merchant schema |
| Payment | Payment and Payment Attempts | PostgreSQL Payment schema |
| Provider Integrations | PSP correlation/reference and mappings | Provider-owned tables |
| Refund | Refund records and states | Refund-owned tables |
| Reconciliation | Reports, runs, discrepancies | Reconciliation-owned tables |
| Notifications | Delivery attempts | Notification-owned tables |

Do not let a module write another context's private tables; prefer explicit contracts.
