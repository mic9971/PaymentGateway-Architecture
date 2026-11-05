# High-Level Design (HLD) — Not Approved

Candidate components: Merchant → Edge/API → payment domain / routing / provider adapters → PostgreSQL (source of truth). Transactional outbox publisher → RabbitMQ → worker consumers. Redis caches non-authoritative data.

First validate FR/NFR, security boundaries, PSP semantics and ADR before committing topology.

Required diagrams: C4 Context, Containers, Components, Payment/Refund Sequences, Trust Boundaries, Deployment.
