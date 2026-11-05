# 08 — ASP.NET Core / C# Contract Design (Illustrative, Not Yet Built)

The following C# is **design pseudocode** to clarify module responsibilities. It is not yet an API project, compiled code or PSP production integration.

## Proposed application model

~~~csharp
public enum PaymentState
{
    Created, Pending, Processing, RequiresAction,
    Unknown, Succeeded, Failed, ExpiredPendingReview, Investigation
}

public sealed record CreatePaymentRequest(
    string MerchantReference,
    long AmountMinor,
    string Currency,
    string PaymentMethod);

public sealed record PaymentReadModel(
    Guid PaymentId,
    PaymentState State,
    long AmountMinor,
    string Currency,
    string? NextActionUrl);

public sealed record CreatePaymentCommand(
    Guid AuthenticatedMerchantId, // derived from auth context, not JSON body
    string IdempotencyKey,
    CreatePaymentRequest Request);
~~~

## Correctness contract boundaries

~~~csharp
public interface ICreatePaymentUseCase
{
    Task<PaymentReadModel> ExecuteAsync(
        CreatePaymentCommand command,
        CancellationToken cancellationToken);
}

// This store performs a SINGLE local PostgreSQL transaction:
// idempotency claim + PaymentIntent + Attempt + Outbox.
public interface IAtomicPaymentCreationStore
{
    Task<CreatePaymentStoreResult> CreateOrGetAsync(
        Guid merchantId,
        string operation,
        string idempotencyKey,
        byte[] canonicalRequestHash,
        CreatePaymentRequest request,
        CancellationToken cancellationToken);
}

// ProviderIntegration BC owns the adapter, not the Payment aggregate.
public interface IPaymentProviderAdapter
{
    Task<ProviderInitiationOutcome> InitiateAsync(
        ProviderInitiationRequest request,
        CancellationToken cancellationToken);

    Task<ProviderInquiryOutcome> QueryStatusAsync(
        ProviderInquiryRequest request,
        CancellationToken cancellationToken);
}
~~~

\`CreatePaymentStoreResult\`, \`ProviderInitiationOutcome\`, and related DTOs deliberately need explicit definitions after PSP contracts are known. No \`Task<bool>\` is allowed to conflate Accepted, Paid, Declined and Unknown.

## Proposed normalized PSP result

~~~csharp
public enum ProviderOutcomeKind
{
    ActionRequired,
    DefinitiveSuccess,
    DefinitiveFailure,
    Ambiguous
}

public sealed record ProviderInitiationOutcome(
    ProviderOutcomeKind Kind,
    string? ProviderReference,
    string? NextActionUrl,
    string EvidenceReference,
    string? ErrorClass);
~~~

This is a **mapping contract**, not evidence that providers share uniform behavior. Provider-specific adapters must distinguish "QR created" from "funds paid", and preserve signature/inquiry evidence.

## Architectural dependency rules

~~~text
Gateway.Api
    ↓
Payments.Application → Payments.Domain
    ↓
Payments.Contracts / permitted public contracts of Merchant/Routing/Providers
    ↓
Payments.Infrastructure (via dependency inversion)

ProviderIntegrations.Infrastructure → PSP API
Messaging.Infrastructure → PostgreSQL Outbox + RabbitMQ
Notifications.Application → Merchant delivery contracts
~~~

- Domain must not import EF Core/Npgsql, ASP.NET, RabbitMQ, Redis or PSP SDK types.
- Payment cannot directly write \`refund.*\`, \`merchant.*\` or \`integration.*\` private tables.
- Routing returns policy decision but Payment enforces safe attempt state.
- Provider adapter returns normalized external outcome; **Payment** applies state transition.
- Controllers do not own transactions; application/use-case boundary coordinates them.

## Runtime version decision

The request uses .NET 8 for study. Microsoft lists **.NET 8 LTS support through November 11, 2026**, so a new six-month production project starting now should explicitly evaluate .NET 10 LTS instead. Do not silently ship an unsupported runtime. Source: https://learn.microsoft.com/en-us/lifecycle/products/microsoft-net-and-net-core

## Before scaffolding actual projects

Confirm architecture decisions ADR-0014 (Attempt aggregate), ADR-0015 (PSP recovery), ADR-0013 (RefundBudget), provider capability contracts, approved C# runtime, test environment and acceptance criteria. No real money-moving API should be deployed before those gates.
