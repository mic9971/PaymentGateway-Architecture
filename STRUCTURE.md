# Repository Structure — Review Draft

This repository is a **documentation-first scaffold (v0.1)**. No production payment code has been built. Module boundaries, platform choices and technical decisions are proposals, not approvals.

## Workflow
Customer Brief → Discovery → Assessment → Domain Design → Architecture Proposal → Solution Design → Validation → Delivery.

## Key folders
- `docs/00-customer-brief`: customer's request and unresolved input.
- `docs/01-requirement-discovery`: stakeholder interviews, requirements.
- `docs/02-architecture-assessment`: risks, assumptions, NFRs and constraints.
- `docs/03-domain-design`: DDD and data ownership.
- `docs/04-solution-proposal`: options, ADRs, cost and approval.
- `docs/05-solution-design`: HLD, data, security, API/event contracts.
- `docs/06-architecture-validation`: evidence and conformance.
- `docs/07-delivery-and-evolution`: roadmap, go-live and debt.
- `src/`: future .NET 8 API/Worker, business modules, building blocks.
- `contracts/`: versioned external contracts.
- `tests/`: unit, integration, architecture, performance, resilience and E2E.
- `deploy/`: deployment, monitoring and infrastructure.

## Complete file inventory (94 files)
```text
.editorconfig
.github/workflows/README.md
.gitignore
README.md
STRUCTURE.md
contracts/events/README.md
contracts/http/README.md
deploy/compose/README.md
deploy/infrastructure/README.md
deploy/monitoring/README.md
docs/00-customer-brief/README.md
docs/00-customer-brief/customer-brief.md
docs/01-requirement-discovery/README.md
docs/01-requirement-discovery/requirements-catalog.md
docs/01-requirement-discovery/stakeholder-interview.md
docs/02-architecture-assessment/README.md
docs/02-architecture-assessment/assumption-register.md
docs/02-architecture-assessment/constraints.md
docs/02-architecture-assessment/quality-attribute-scenarios.md
docs/02-architecture-assessment/risk-register.md
docs/03-domain-design/README.md
docs/03-domain-design/business-capabilities.md
docs/03-domain-design/context-map.md
docs/03-domain-design/data-ownership.md
docs/04-solution-proposal/README.md
docs/04-solution-proposal/adrs/ADR-0001-application-style.md
docs/04-solution-proposal/cost-estimate.md
docs/04-solution-proposal/proposal.md
docs/04-solution-proposal/solution-options.md
docs/05-solution-design/README.md
docs/05-solution-design/api-and-event-contracts.md
docs/05-solution-design/database-design.md
docs/05-solution-design/hld.md
docs/05-solution-design/payment-state-machine.md
docs/05-solution-design/security-and-deployment.md
docs/06-architecture-validation/README.md
docs/06-architecture-validation/correctness-tests.md
docs/06-architecture-validation/fitness-functions.md
docs/06-architecture-validation/performance-evidence.md
docs/06-architecture-validation/reliability-and-recovery.md
docs/06-architecture-validation/workload-and-capacity.md
docs/07-delivery-and-evolution/README.md
docs/07-delivery-and-evolution/go-live-checklist.md
docs/07-delivery-and-evolution/roadmap.md
docs/07-delivery-and-evolution/technical-debt.md
scripts/README.md
src/BuildingBlocks/Caching/README.md
src/BuildingBlocks/Messaging/README.md
src/BuildingBlocks/Observability/README.md
src/BuildingBlocks/Persistence/README.md
src/BuildingBlocks/Security/README.md
src/Gateway.Api/README.md
src/Gateway.Worker/README.md
src/Modules/Merchants/Application/.gitkeep
src/Modules/Merchants/Contracts/.gitkeep
src/Modules/Merchants/Domain/.gitkeep
src/Modules/Merchants/Infrastructure/.gitkeep
src/Modules/Merchants/README.md
src/Modules/Notifications/Application/.gitkeep
src/Modules/Notifications/Contracts/.gitkeep
src/Modules/Notifications/Domain/.gitkeep
src/Modules/Notifications/Infrastructure/.gitkeep
src/Modules/Notifications/README.md
src/Modules/Payments/Application/.gitkeep
src/Modules/Payments/Contracts/.gitkeep
src/Modules/Payments/Domain/.gitkeep
src/Modules/Payments/Infrastructure/.gitkeep
src/Modules/Payments/README.md
src/Modules/ProviderIntegrations/Application/.gitkeep
src/Modules/ProviderIntegrations/Contracts/.gitkeep
src/Modules/ProviderIntegrations/Domain/.gitkeep
src/Modules/ProviderIntegrations/Infrastructure/.gitkeep
src/Modules/ProviderIntegrations/README.md
src/Modules/Reconciliation/Application/.gitkeep
src/Modules/Reconciliation/Contracts/.gitkeep
src/Modules/Reconciliation/Domain/.gitkeep
src/Modules/Reconciliation/Infrastructure/.gitkeep
src/Modules/Reconciliation/README.md
src/Modules/Refunds/Application/.gitkeep
src/Modules/Refunds/Contracts/.gitkeep
src/Modules/Refunds/Domain/.gitkeep
src/Modules/Refunds/Infrastructure/.gitkeep
src/Modules/Refunds/README.md
src/Modules/Routing/Application/.gitkeep
src/Modules/Routing/Contracts/.gitkeep
src/Modules/Routing/Domain/.gitkeep
src/Modules/Routing/Infrastructure/.gitkeep
src/Modules/Routing/README.md
tests/ArchitectureTests/README.md
tests/E2ETests/README.md
tests/IntegrationTests/README.md
tests/PerformanceTests/README.md
tests/ResilienceTests/README.md
tests/UnitTests/README.md
```
