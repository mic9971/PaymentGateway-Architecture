# ProviderIntegrations Bounded Context — Proposed

- Domain: aggregates, invariants, domain events.
- Application: commands, queries and business workflows.
- Infrastructure: repositories and external adapters.
- Contracts: explicit APIs/events exposed to other contexts.

No module may reach into another module's private Infrastructure. Ownership and contracts are subject to domain review.
