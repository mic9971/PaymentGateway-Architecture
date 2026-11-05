using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PaymentGateway.Payments.Domain;

namespace PaymentGateway.Payments.Application;

public sealed record CreatePaymentCommand(
    Guid AuthenticatedMerchantId,
    string IdempotencyKey,
    string MerchantReference,
    long AmountMinor,
    string Currency,
    string PaymentMethod);

public sealed record CreationResult(PaymentSnapshot Payment, bool Replayed);
public sealed class IdempotencyConflictException : Exception
{
    public IdempotencyConflictException()
        : base("This Idempotency-Key was already used with a different request.") { }
}

public interface IPaymentStore
{
    Task<CreationResult> CreateOrGetAsync(
        CreatePaymentCommand command, byte[] requestHash, CancellationToken cancellationToken);
    Task<PaymentSnapshot?> GetAsync(Guid merchantId, Guid paymentId, CancellationToken cancellationToken);
}

/// <summary>Validation + orchestration; atomicity belongs to IPaymentStore.</summary>
public sealed class PaymentService(IPaymentStore store)
{
    public async Task<CreationResult> CreateAsync(CreatePaymentCommand request, CancellationToken ct)
    {
        if (request.AuthenticatedMerchantId == Guid.Empty)
            throw new ArgumentException("Merchant identity is required.");
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey)
            || request.IdempotencyKey.Length is < 8 or > 128
            || !request.IdempotencyKey.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))
            throw new ArgumentException("Idempotency-Key must be 8–128 ASCII letters, digits, '-', '_' or '.'.");
        if (string.IsNullOrWhiteSpace(request.MerchantReference) || request.MerchantReference.Length > 100)
            throw new ArgumentException("MerchantReference is required (max 100 chars).");
        if (request.AmountMinor is <= 0 or > 1_000_000_000_000L)
            throw new ArgumentException("AmountMinor must be a positive supported amount.");
        if (request.Currency != "VND" || request.PaymentMethod != "MOCK")
            throw new ArgumentException("Only currency VND and payment method MOCK are enabled in this demo.");

        // Canonical request fingerprint; merchant identity + operation are also database key scope.
        var stableJson = JsonSerializer.Serialize(new[]
        {
            request.MerchantReference, request.AmountMinor.ToString(System.Globalization.CultureInfo.InvariantCulture),
            request.Currency, request.PaymentMethod
        });
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(stableJson));
        return await store.CreateOrGetAsync(request, hash, ct);
    }

    public Task<PaymentSnapshot?> GetAsync(Guid authenticatedMerchantId, Guid paymentId, CancellationToken ct)
        => store.GetAsync(authenticatedMerchantId, paymentId, ct);
}
