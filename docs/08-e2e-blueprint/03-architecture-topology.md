# 03 — C4 Logical Architecture, Trust Boundaries and Deployment Hypothesis

## System Context

~~~mermaid
flowchart LR
    Buyer["Payer / Customer"] --> Shop["Merchant App"]
    Shop --> PG["Payment Gateway"]
    PG --> Partner["PSP / Bank / Wallet Partner"]
    Partner --> PG
    PG --> MerchantWebhook["Merchant Webhook Endpoint"]
    Finance["Finance / Operations"] --> Ops["Operator Portal (candidate)"]
    Ops --> PG
~~~

**Money stays in externally contracted financial rails unless a separate funds-management model is approved.** Gateway payment records are internal orchestration evidence, not a proof of actual bank settlement.

## Containers / Components (candidate)

~~~mermaid
flowchart TB
    Merchant["Merchant / Client"] --> Edge["Edge / WAF / API Gateway"]
    PSP["PSP / Bank / Wallet"] -->|Authenticated callback| Edge
    Edge --> API["ASP.NET Core Payment API"]
    subgraph APP["Gateway boundary"]
      API --> Core["Payment Application (Modular Monolith)"]
      Core --> Routing["Routing Policy"]
      Core --> Adapter["Provider Adapter"]
      Core --> PG[("PostgreSQL: operational truth")]
      Core --> Redis[("Redis: non-authoritative cache")]
      PG --> Outbox["Outbox Publisher"]
      Outbox --> MQ[("RabbitMQ")]
      MQ --> Workers["Payment / Notification Workers"]
      Workers --> PG
      Recon["Status Inquiry / Reconciliation Workers"] --> PG
      Workers --> Adapter
      Recon --> Adapter
    end
    Adapter -->|HTTPS| PSP
    Workers -->|Signed event callback| MerchantWebhook["Merchant Webhook Endpoint"]
    API --> OTEL["Observability pipeline"]
    Workers --> OTEL
    Recon --> OTEL
~~~

## Trust boundaries

- **TB-01 Internet → Edge:** Merchant authentication, request validation, rate limiting, tenant identity.
- **TB-02 PSP → Inbound Webhook:** Per-provider signature, timestamp/replay, maximum payload, sender identity as applicable.
- **TB-03 App → PSP:** Outbound TLS, credentials from secrets store, request identifiers and safe timeout classification.
- **TB-04 Services → PostgreSQL:** Least privilege, schema ownership, encryption and backup.
- **TB-05 Workers ↔ MQ/Redis:** Separate credentials, bounded message size, no sensitive payload when reference suffices.
- **TB-06 Ops → financial records:** RBAC, audit, maker-checker where required; no arbitrary status edits.

## Physical deployment hypothesis

~~~mermaid
flowchart TD
    Net["HTTPS edge"] --> AZ["Compute / deployment zone (single region candidate)"]
    AZ --> A["API replicas N >= 2 (placeholder)"]
    AZ --> W["Background workers"]
    A --> DB[("PostgreSQL HA candidate")]
    W --> DB
    W --> MQ[("RabbitMQ HA candidate")]
    A --> C[("Redis candidate")]
    W --> Obs["Metrics / logs / traces"]
    A --> Obs
    Backup["Backup / PITR"] --> DB
~~~

These are **hypotheses**, not verified instance counts or HA design. Region, tenancy, AZ/failure topology, backup frequency, health probes, cost, RTO/RPO, and production runtime remain open.

## Why not prematurely shard or introduce multi-region active-active?

Higher consistency and operational costs can be unacceptable in financial correctness workflows. First validate locality, actual peak load, data sovereignty, RPO/RTO, conflict rules and live provider routing. No cross-region write conflict behavior is approved in this draft.
