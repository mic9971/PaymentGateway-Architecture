using Npgsql;
using PaymentGateway.Payments.Application;
using PaymentGateway.Payments.Infrastructure;
using Xunit;

namespace PaymentGateway.Tests.Integration;

/// <summary>
/// Requires PostgreSQL with deploy/compose/postgres-init/001_payment_core.sql applied.
/// GitHub Actions supplies PAYMENT_TEST_CONNECTION_STRING; locally, omit to run unit tests only.
/// </summary>
public sealed class PaymentIdempotencyIntegrationTests
{
    private static string? ConnectionString =>
        Environment.GetEnvironmentVariable("PAYMENT_TEST_CONNECTION_STRING");

    [Fact]
    public async Task ConcurrentSameKey_ReturnsSinglePayment_AndSingleOutboxRecord()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString)) return; // No local integration database configured.
        await using var ds = NpgsqlDataSource.Create(ConnectionString);
        var service = new PaymentService(new NpgsqlPaymentStore(ds));
        var merchant = Guid.NewGuid();
        var idem = "concurrent-" + Guid.NewGuid().ToString("N");
        var command = new CreatePaymentCommand(merchant, idem, "order-demo",
            500000, "VND", "MOCK");

        var results = await Task.WhenAll(
            Enumerable.Range(0, 12).Select(_ => service.CreateAsync(command, CancellationToken.None)));

        var paymentId = results[0].Payment.PaymentId;
        Assert.All(results, r => Assert.Equal(paymentId, r.Payment.PaymentId));
        Assert.Equal(1, results.Count(r => !r.Replayed));
        Assert.Equal(11, results.Count(r => r.Replayed));

        await using var conn = await ds.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT count(*) FROM messaging.outbox_messages
            WHERE aggregate_id = @payment AND event_type='PaymentRequested'
            """, conn);
        cmd.Parameters.AddWithValue("payment", paymentId);
        Assert.Equal(1L, (long)(await cmd.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task SameKeyDifferentPayload_ReturnsConflict()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString)) return;
        await using var ds = NpgsqlDataSource.Create(ConnectionString);
        var service = new PaymentService(new NpgsqlPaymentStore(ds));
        var merchant = Guid.NewGuid();
        var key = "different-" + Guid.NewGuid().ToString("N");
        var first = new CreatePaymentCommand(merchant, key, "order-demo", 500000, "VND", "MOCK");
        var second = first with { AmountMinor = 600000 };

        await service.CreateAsync(first, CancellationToken.None);
        await Assert.ThrowsAsync<IdempotencyConflictException>(
            () => service.CreateAsync(second, CancellationToken.None));
    }

    [Fact]
    public async Task MerchantCannotReadAnotherMerchantsPayment()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString)) return;
        await using var ds = NpgsqlDataSource.Create(ConnectionString);
        var service = new PaymentService(new NpgsqlPaymentStore(ds));
        var merchant = Guid.NewGuid();
        var command = new CreatePaymentCommand(merchant, "tenant-" + Guid.NewGuid().ToString("N"),
            "order-demo", 500000, "VND", "MOCK");

        var result = await service.CreateAsync(command, CancellationToken.None);
        var fromOtherMerchant = await service.GetAsync(Guid.NewGuid(), result.Payment.PaymentId, CancellationToken.None);
        Assert.Null(fromOtherMerchant);
    }

    [Fact]
    public async Task MockProcessing_ClaimsOnce_AndDoesNotReuseSameAttempt()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString)) return;
        await using var ds = NpgsqlDataSource.Create(ConnectionString);
        var service = new PaymentService(new NpgsqlPaymentStore(ds));
        var cmd = new CreatePaymentCommand(Guid.NewGuid(), "worker-" + Guid.NewGuid().ToString("N"),
            "order-demo", 500000, "VND", "MOCK");
        var created = await service.CreateAsync(cmd, CancellationToken.None);
        var worker = new NpgsqlPaymentWorkStore(ds);

        // Worker processes global pending queue; repeated claims cannot claim the same attempt.
        var claimed = await worker.ClaimPendingAsync(CancellationToken.None);
        Assert.NotNull(claimed);
        var secondClaim = await worker.ClaimPendingAsync(CancellationToken.None);
        Assert.True(claimed!.AttemptId != secondClaim?.AttemptId);

        var completed = await worker.CompleteAsync(claimed, PaymentGateway.Payments.Domain.PaymentState.Unknown, CancellationToken.None);
        Assert.True(completed);
        var completedAgain = await worker.CompleteAsync(claimed, PaymentGateway.Payments.Domain.PaymentState.Succeeded, CancellationToken.None);
        Assert.False(completedAgain);
    }
}
