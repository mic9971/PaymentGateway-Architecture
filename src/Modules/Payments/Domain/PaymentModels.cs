namespace PaymentGateway.Payments.Domain;

/// <summary>Gateway orchestration state, NOT a bank settlement ledger.</summary>
public enum PaymentState
{
    Pending, Processing, RequiresAction, Succeeded, Failed, Unknown
}

public sealed record PaymentSnapshot(
    Guid PaymentId,
    Guid MerchantId,
    string MerchantReference,
    long AmountMinor,
    string Currency,
    string PaymentMethod,
    PaymentState State,
    long Version,
    DateTimeOffset CreatedAt);

public static class PaymentTransitions
{
    public static bool Allows(PaymentState current, PaymentState next) => (current, next) switch
    {
        (PaymentState.Pending, PaymentState.Processing) => true,
        (PaymentState.Processing, PaymentState.Succeeded or PaymentState.Failed or PaymentState.Unknown or PaymentState.RequiresAction) => true,
        (PaymentState.RequiresAction, PaymentState.Succeeded or PaymentState.Unknown) => true,
        (PaymentState.Unknown, PaymentState.Succeeded or PaymentState.Failed) => true,
        _ => false
    };
}
