# PostgreSQL Data Ownership & .NET Modular Structure

## Write ownership proposal

| Context | Owns PostgreSQL logical schema/tables | No other context may directly write |
|---|---|---|
| Merchant | merchant.merchants, merchant.api_credentials, merchant.merchant_settings | merchant.* |
| Payment | payment.payments, payment.payment_attempts, payment.idempotency_records | payment.* |
| Routing | routing.routing_rules, routing.routing_decisions | routing.* |
| Provider Integration | integration.provider_correlations, integration.webhook_inbox | integration.* |
| Refund | refund.refunds, refund.refund_attempts, refund.payment_refund_budget (option A, not approved) | refund.* |
| Reconciliation | reconciliation.runs, reconciliation.items, reconciliation.discrepancies | reconciliation.* |
| Notifications | notification.deliveries, notification.attempts | notification.* |
| Audit & Ops | audit.audit_entries, operations.cases | audit.*, operations.* (boundary under review) |

**Important:** A physical PostgreSQL schema is not a bounded context; it is a deployment/storage mechanism. One BC could span physical stores, and several can coexist in one database, while preserving explicit write ownership.

## Shared Infrastructure exception

Transactional Outbox and consumer Inbox records live in the candidate \`messaging\` technical schema. These are NOT a ninth Bounded Context. Each business module may use a shared Outbox library to insert publication intent **inside its own business transaction**; database privileges and tenant isolation must be designed so this is a controlled exception rather than arbitrary cross-context writes.

Audit event emission is similarly cross-cutting. Operator **investigation and resolution** are business workflows and potentially a dedicated context, if their language and ownership justify one.

## Suggested logical .NET repo shape (after design approval)

~~~text
src/
  Gateway.Api/
  Gateway.Worker/
  Modules/
    Merchants/
      Domain/
      Application/
      Contracts/
      Infrastructure/
    Payments/
      Domain/
      Application/
      Contracts/
      Infrastructure/
    Routing/
      Domain/
      Application/
      Contracts/
      Infrastructure/
    ProviderIntegrations/
      Domain/
      Application/
      Contracts/
      Infrastructure/
      Adapters/
        VietQrPartner/
        EWalletPartner/
        OtherPsp/
    Refunds/
      Domain/
      Application/
      Contracts/
      Infrastructure/
    Reconciliation/
      Domain/
      Application/
      Contracts/
      Infrastructure/
    Notifications/
      Domain/
      Application/
      Contracts/
      Infrastructure/
    Operations/
      Domain/
      Application/
      Contracts/
      Infrastructure/
  BuildingBlocks/
    Messaging/
    Persistence/
    Caching/
    Security/
    Observability/
tests/
  UnitTests/
  IntegrationTests/
  ArchitectureTests/
  ResilienceTests/
  PerformanceTests/
  E2ETests/
~~~

These directory names are conceptual and may be implemented within fewer physical projects for easier deployment. **No code or database migration is yet implemented.**

## Dependency rules to automate

1. \`*.Domain\` cannot reference Infrastructure, EF Core, ASP.NET or RabbitMQ packages.
2. \`ModuleA\` must not reference \`ModuleB.Infrastructure\` or \`ModuleB.Domain\`; only public \`ModuleB.Contracts\` (or the approved application service contract).
3. Modules do not write another context's private tables.
4. Provider-adapter concrete types cannot leak into the Payment aggregate or public merchant API.
5. Refund must use the approved atomic budget reservation contract, not a stale read and blind PSP call.
6. Outbox/inbox contracts, schema versions, tenant identity and security tests run in CI.
7. Cross-context exceptions require explicit ADR and review.

## Deployment mapping

Initially deploy API + background workers, PostgreSQL, RabbitMQ and possibly Redis if justified by measured workload. Splitting a logical context into an independently deployable microservice requires its own reliability/cost/consistency review.
