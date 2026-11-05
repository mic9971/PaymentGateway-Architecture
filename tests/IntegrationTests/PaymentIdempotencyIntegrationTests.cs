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


    [Fact]
    public async Task IdempotencyKeyIsScopedToAuthenticatedMerchant()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString)) return;
        await using var ds = NpgsqlDataSource.Create(ConnectionString);
        var service = new PaymentService(new NpgsqlPaymentStore(ds));
        var sharedKey = "same-key-" + Guid.NewGuid().ToString("N");
        var first = new CreatePaymentCommand(Guid.NewGuid(), sharedKey, "order-demo", 100, "VND", "MOCK");
        var second = first with { AuthenticatedMerchantId = Guid.NewGuid() };

        var a = await service.CreateAsync(first, CancellationToken.None);
        var b = await service.CreateAsync(second, CancellationToken.None);
        Assert.NotEqual(a.Payment.PaymentId, b.Payment.PaymentId);
        Assert.False(a.Replayed);
        Assert.False(b.Replayed);
    }

    [Fact]
    public async Task ExpiredAttemptIsUnknownAndNotReissued()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString)) return;
        await using var ds = NpgsqlDataSource.Create(ConnectionString);
        var service = new PaymentService(new NpgsqlPaymentStore(ds));
        var command = new CreatePaymentCommand(Guid.NewGuid(), "crash-" + Guid.NewGuid().ToString("N"),
            "crashed-worker-demo", 500000, "VND", "MOCK");
        var created = await service.CreateAsync(command, CancellationToken.None);

        // Simulate worker committing a claim, then crashing after an uncertain PSP operation.
        await using (var conn = await ds.OpenConnectionAsync())
        {
            await using var tx = await conn.BeginTransactionAsync();
            await using (var attempt = new NpgsqlCommand("""
                UPDATE payment.payment_attempts SET state='Processing',
                    claim_id=@claim, lease_until=now() - interval '1 minute'
                WHERE payment_id=@payment AND state='Pending'
                """, conn, tx))
            {
                attempt.Parameters.AddWithValue("payment", created.Payment.PaymentId);
                attempt.Parameters.AddWithValue("claim", Guid.NewGuid());
                Assert.Equal(1, await attempt.ExecuteNonQueryAsync());
            }
            await using (var pay = new NpgsqlCommand("""
                UPDATE payment.payment_intents SET state='Processing', version=version+1
                WHERE payment_id=@payment AND state='Pending'
                """, conn, tx))
            {
                pay.Parameters.AddWithValue("payment", created.Payment.PaymentId);
                Assert.Equal(1, await pay.ExecuteNonQueryAsync());
            }
            await tx.CommitAsync();
        }

        var worker = new NpgsqlPaymentWorkStore(ds);
        Assert.True(await worker.MarkOneExpiredClaimUnknownAsync(CancellationToken.None));
        var after = await service.GetAsync(command.AuthenticatedMerchantId, created.Payment.PaymentId, CancellationToken.None);
        Assert.NotNull(after);
        Assert.Equal(PaymentGateway.Payments.Domain.PaymentState.Unknown, after!.State);

        await using var checkConn = await ds.OpenConnectionAsync();
        await using var check = new NpgsqlCommand("""
            SELECT state FROM payment.payment_attempts WHERE payment_id=@payment
            """, checkConn);
        check.Parameters.AddWithValue("payment", created.Payment.PaymentId);
        Assert.Equal("Unknown", (string)(await check.ExecuteScalarAsync())!);
    }

}
