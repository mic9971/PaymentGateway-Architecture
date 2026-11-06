using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PaymentGateway.Refunds.Domain;

namespace PaymentGateway.Refunds.Application;

public sealed record RequestRefundCommand(
    Guid AuthenticatedMerchantId, Guid PaymentId, string IdempotencyKey, long AmountMinor);

public sealed record RefundCreationResult(RefundSnapshot Refund, bool Replayed);

public sealed class RefundConflictException(string reason) : Exception(reason);
public sealed class InsufficientRefundBudgetException() : Exception("Refund exceeds verified refundable budget.");
public sealed class PaymentNotRefundableException() : Exception("Payment is not currently refundable.");

public interface IRefundStore
{
    Task<RefundCreationResult> CreateOrGetAsync(
        RequestRefundCommand command, byte[] fingerprint, CancellationToken ct);
    Task<RefundSnapshot?> GetAsync(Guid merchantId, Guid refundId, CancellationToken ct);
}

public sealed class RefundService(IRefundStore store)
{
    public async Task<RefundCreationResult> RequestAsync(RequestRefundCommand command, CancellationToken ct)
    {
        if (command.AuthenticatedMerchantId == Guid.Empty || command.PaymentId == Guid.Empty)
            throw new ArgumentException("Authenticated merchant and payment are required.");
        if (command.AmountMinor is <= 0 or > 1_000_000_000_000L)
            throw new ArgumentException("Refund amount is invalid.");
        if (string.IsNullOrWhiteSpace(command.IdempotencyKey)
            || command.IdempotencyKey.Length is < 8 or > 128
            || !command.IdempotencyKey.All(x=>char.IsAsciiLetterOrDigit(x) || x is '-' or '_' or '.'))
            throw new ArgumentException("Invalid Idempotency-Key.");
        var canonical = JsonSerializer.Serialize(new[]{
            command.PaymentId.ToString("D"),
            command.AmountMinor.ToString(CultureInfo.InvariantCulture)
        });
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return await store.CreateOrGetAsync(command, hash, ct);
    }

    public Task<RefundSnapshot?> GetAsync(Guid merchant, Guid refundId, CancellationToken ct)
        => store.GetAsync(merchant, refundId, ct);
}
