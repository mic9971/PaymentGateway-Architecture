using Npgsql;
using PaymentGateway.Payments.Application;
using PaymentGateway.Payments.Domain;
using PaymentGateway.Payments.Infrastructure;
using Xunit;

namespace PaymentGateway.Tests.Integration;

/// <summary>Mock PSP persists its effect separately from the gateway's payment state.</summary>
public sealed class PaymentRecoveryIntegrationTests
{
    private static string? Pg => Environment.GetEnvironmentVariable("PAYMENT_TEST_CONNECTION_STRING");

    [Fact]
    public async Task PspCommittedSuccess_ButGatewayTimedOut_InquiryReconcilesWithoutNewCharge()
    {
        if (string.IsNullOrWhiteSpace(Pg)) return;
        await using var ds = NpgsqlDataSource.Create(Pg);
        var gateway = new PaymentService(new NpgsqlPaymentStore(ds));
        var provider = new NpgsqlMockPspLedger(ds);
        var created = await CreateAsync(gateway);
        var claim = await ClaimSpecificPaymentAsync(ds, created.Payment.PaymentId);

        Assert.Equal(PaymentState.Unknown,
            await provider.InitiateAsync(claim.ProviderOperationKey,
                MockPspBehavior.TimeoutAfterSuccess, CancellationToken.None));

        // PSP has already saved Succeeded, while the gateway knows only Unknown.
        Assert.Equal(PaymentState.Succeeded,
            await provider.QueryAsync(claim.ProviderOperationKey, CancellationToken.None));
        Assert.True(await new NpgsqlPaymentWorkStore(ds)
            .CompleteAsync(claim, PaymentState.Unknown, CancellationToken.None));

        var pending = await gateway.GetAsync(created.Payment.MerchantId, created.Payment.PaymentId, CancellationToken.None);
        Assert.Equal(PaymentState.Unknown, pending!.State);

        var recovery = new PaymentRecoveryService(
            new NpgsqlUnknownRecoveryStore(ds), provider);
        Assert.Equal(RecoveryResult.VerifiedSuccess,
            await recovery.RecoverPaymentAsync(created.Payment.PaymentId, CancellationToken.None));

        var resolved = await gateway.GetAsync(created.Payment.MerchantId, created.Payment.PaymentId, CancellationToken.None);
        Assert.Equal(PaymentState.Succeeded, resolved!.State);
        Assert.Equal(RecoveryResult.NoWork,
            await recovery.RecoverPaymentAsync(created.Payment.PaymentId, CancellationToken.None));

        await using var db = await ds.OpenConnectionAsync();
        await using var count = new NpgsqlCommand("""
            SELECT count(*) FROM mockpsp.provider_operations
            WHERE provider_operation_key=@key
            """, db);
        count.Parameters.AddWithValue("key", claim.ProviderOperationKey);
        Assert.Equal(1L, (long)(await count.ExecuteScalarAsync())!);

        await using var emitted = new NpgsqlCommand("""
            SELECT count(*) FROM messaging.outbox_messages
            WHERE aggregate_id=@payment AND event_type='PaymentSucceeded'
            """, db);
        emitted.Parameters.AddWithValue("payment", created.Payment.PaymentId);
        Assert.Equal(1L, (long)(await emitted.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task TimeoutBeforeProviderEffect_StaysUnknownAfterInquiry()
    {
        if (string.IsNullOrWhiteSpace(Pg)) return;
        await using var ds = NpgsqlDataSource.Create(Pg);
        var gateway = new PaymentService(new NpgsqlPaymentStore(ds));
        var provider = new NpgsqlMockPspLedger(ds);
        var created = await CreateAsync(gateway);
        var claim = await ClaimSpecificPaymentAsync(ds, created.Payment.PaymentId);

        Assert.Equal(PaymentState.Unknown,
            await provider.InitiateAsync(claim.ProviderOperationKey,
                MockPspBehavior.TimeoutBeforeEffect, CancellationToken.None));
        Assert.True(await new NpgsqlPaymentWorkStore(ds)
            .CompleteAsync(claim, PaymentState.Unknown, CancellationToken.None));

        var recovery = new PaymentRecoveryService(new NpgsqlUnknownRecoveryStore(ds), provider);
        Assert.Equal(RecoveryResult.Unresolved,
            await recovery.RecoverPaymentAsync(created.Payment.PaymentId, CancellationToken.None));
        Assert.Null(await provider.QueryAsync(claim.ProviderOperationKey, CancellationToken.None));

        var stillUnknown = await gateway.GetAsync(created.Payment.MerchantId, created.Payment.PaymentId, CancellationToken.None);
        Assert.Equal(PaymentState.Unknown, stillUnknown!.State);
        Assert.Equal(RecoveryResult.NoWork,
            await recovery.RecoverPaymentAsync(created.Payment.PaymentId, CancellationToken.None));
    }

    [Fact]
    public async Task MockPsp_SameStableOperationKey_DoesNotOverwriteFirstEffect()
    {
        if (string.IsNullOrWhiteSpace(Pg)) return;
        await using var ds = NpgsqlDataSource.Create(Pg);
        var provider = new NpgsqlMockPspLedger(ds);
        var key = "mock-idem-" + Guid.NewGuid().ToString("N");

        Assert.Equal(PaymentState.Succeeded,
            await provider.InitiateAsync(key, MockPspBehavior.Succeeded, CancellationToken.None));
        Assert.Equal(PaymentState.Succeeded,
            await provider.InitiateAsync(key, MockPspBehavior.Failed, CancellationToken.None));
        Assert.Equal(PaymentState.Succeeded,
            await provider.QueryAsync(key, CancellationToken.None));

        await using var conn = await ds.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT count(*) FROM mockpsp.provider_operations WHERE provider_operation_key=@key
            """, conn);
        cmd.Parameters.AddWithValue("key", key);
        Assert.Equal(1L, (long)(await cmd.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task ConcurrentInquiries_CommitOnlyOneVerifiedTransition()
    {
        if (string.IsNullOrWhiteSpace(Pg)) return;
        await using var ds = NpgsqlDataSource.Create(Pg);
        var gateway = new PaymentService(new NpgsqlPaymentStore(ds));
        var provider = new NpgsqlMockPspLedger(ds);
        var created = await CreateAsync(gateway);
        var claim = await ClaimSpecificPaymentAsync(ds, created.Payment.PaymentId);

        await provider.InitiateAsync(claim.ProviderOperationKey,
            MockPspBehavior.TimeoutAfterSuccess, CancellationToken.None);
        Assert.True(await new NpgsqlPaymentWorkStore(ds)
            .CompleteAsync(claim, PaymentState.Unknown, CancellationToken.None));

        var recovery = new PaymentRecoveryService(new NpgsqlUnknownRecoveryStore(ds), provider);
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ =>
            recovery.RecoverPaymentAsync(created.Payment.PaymentId, CancellationToken.None)));
        Assert.Equal(1, outcomes.Count(x => x == RecoveryResult.VerifiedSuccess));
        Assert.All(outcomes, x => Assert.True(x is RecoveryResult.VerifiedSuccess or RecoveryResult.NoWork));

        await using var conn = await ds.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT count(*) FROM messaging.outbox_messages
            WHERE aggregate_id=@payment AND event_type='PaymentSucceeded'
            """, conn);
        cmd.Parameters.AddWithValue("payment", created.Payment.PaymentId);
        Assert.Equal(1L, (long)(await cmd.ExecuteScalarAsync())!);
    }

    private static Task<CreationResult> CreateAsync(PaymentService gateway)
        => gateway.CreateAsync(new CreatePaymentCommand(
            Guid.NewGuid(), "recover-" + Guid.NewGuid().ToString("N"), "demo-recovery-order",
            100000, "VND", "MOCK"), CancellationToken.None);

    private static async Task<ClaimedAttempt> ClaimSpecificPaymentAsync(
        NpgsqlDataSource ds, Guid paymentId)
    {
        await using var conn = await ds.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        Guid attemptId;
        string operationKey;
        await using (var select = new NpgsqlCommand("""
            SELECT attempt_id, provider_operation_key
            FROM payment.payment_attempts
            WHERE payment_id=@payment AND state='Pending'
            FOR UPDATE
            """, conn, tx))
        {
            select.Parameters.AddWithValue("payment", paymentId);
            await using var reader = await select.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            attemptId = reader.GetGuid(0);
            operationKey = reader.GetString(1);
        }
        var claim = Guid.NewGuid();
        await using (var attempt = new NpgsqlCommand("""
            UPDATE payment.payment_attempts
            SET state='Processing', claim_id=@claim,
                lease_until=now() + interval '30 seconds'
            WHERE attempt_id=@attempt AND state='Pending'
            """, conn, tx))
        {
            attempt.Parameters.AddWithValue("claim", claim);
            attempt.Parameters.AddWithValue("attempt", attemptId);
            Assert.Equal(1, await attempt.ExecuteNonQueryAsync());
        }
        await using (var payment = new NpgsqlCommand("""
            UPDATE payment.payment_intents
            SET state='Processing', version=version+1
            WHERE payment_id=@payment AND state='Pending'
            """, conn, tx))
        {
            payment.Parameters.AddWithValue("payment", paymentId);
            Assert.Equal(1, await payment.ExecuteNonQueryAsync());
        }
        await tx.CommitAsync();
        return new ClaimedAttempt(attemptId, paymentId, claim, operationKey);
    }
}
