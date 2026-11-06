namespace PaymentGateway.Refunds.Domain;

/// <summary>Separate refund lifecycle; payment success does not equal successful refund.</summary>
public enum RefundState { Requested, Processing, Succeeded, Failed, Unknown }

public sealed record RefundSnapshot(
    Guid RefundId, Guid PaymentId, Guid MerchantId, long AmountMinor,
    string Currency, RefundState State, DateTimeOffset CreatedAt);

public static class RefundTransitions
{
    public static bool Allows(RefundState current, RefundState next) => (current, next) switch
    {
        (RefundState.Requested, RefundState.Processing) => true,
        (RefundState.Processing, RefundState.Succeeded or RefundState.Failed or RefundState.Unknown) => true,
        (RefundState.Unknown, RefundState.Succeeded or RefundState.Failed) => true,
        _ => false
    };
}
