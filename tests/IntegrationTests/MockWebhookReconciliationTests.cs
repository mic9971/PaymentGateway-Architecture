using System.Text;
using System.Text.Json;
using Npgsql;
using PaymentGateway.Payments.Application;
using PaymentGateway.Payments.Domain;
using PaymentGateway.Payments.Infrastructure;
using Xunit;

namespace PaymentGateway.Tests.Integration;

/// <summary>Development-only signed webhook and reconciliation contracts.</summary>
public sealed class MockWebhookReconciliationTests
{
    private static string? Pg => Environment.GetEnvironmentVariable("PAYMENT_TEST_CONNECTION_STRING");
    private static readonly byte[] Secret = Encoding.UTF8.GetBytes("dev-only-unit-test-signing-key-32bytes");

    [Fact]
    public void Signature_VerifiesRawBody_AndRejectsTamperingOrStaleTimestamp()
    {
        var raw = Encoding.UTF8.GetBytes("""{"event_id":"demo","status":"Succeeded"}""");
        var now = DateTimeOffset.UtcNow;
        var timestamp = now.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var signature = MockWebhookSignature.Sign(Secret, timestamp, raw);
        Assert.True(MockWebhookSignature.Verify(Secret, timestamp, signature, raw, now));
        Assert.False(MockWebhookSignature.Verify(Secret, timestamp, signature,
            Encoding.UTF8.GetBytes("""{"event_id":"demo","status":"Failed"}"""), now));
        Assert.False(MockWebhookSignature.Verify(Secret, timestamp, signature[..^1] + (signature[^1] == '0' ? "1" : "0"), raw, now));
        Assert.False(MockWebhookSignature.Verify(Secret, timestamp, "sha256=not-a-signature", raw, now));
        Assert.False(MockWebhookSignature.Verify(Secret, timestamp, signature, raw, now.AddMinutes(10)));
    }

    [Fact]
    public async Task ValidVerifiedWebhook_FromPending_UpdatesStateAndOutboxOnce()
    {
        if (string.IsNullOrWhiteSpace(Pg)) return;
        await using var source = NpgsqlDataSource.Create(Pg);
        var (created, payload) = await NewPayloadAsync(source, "Succeeded");
        var processor = new NpgsqlMockWebhookProcessor(source);
        var raw = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));

        Assert.Equal(MockWebhookDisposition.Applied,
            await processor.ApplyVerifiedAsync(payload, raw, CancellationToken.None));

        var payment = await new PaymentService(new NpgsqlPaymentStore(source)).GetAsync(
            created.Payment.MerchantId, created.Payment.PaymentId, CancellationToken.None);
        Assert.Equal(PaymentState.Succeeded, payment!.State);
        await using var db = await source.OpenConnectionAsync();
        await using var outbox = new NpgsqlCommand("""
            SELECT count(*) FROM messaging.outbox_messages
            WHERE aggregate_id=@payment AND event_type='PaymentSucceeded'
            """, db);
        outbox.Parameters.AddWithValue("payment", created.Payment.PaymentId);
        Assert.Equal(1L, (long)(await outbox.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task DuplicateSameEvent_ConcurrentDelivery_DurableInboxAppliesOnce()
    {
        if (string.IsNullOrWhiteSpace(Pg)) return;
        await using var source = NpgsqlDataSource.Create(Pg);
        var (created, payload) = await NewPayloadAsync(source, "Succeeded");
        var raw = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
        var processor = new NpgsqlMockWebhookProcessor(source);
        var outcomes = await Task.WhenAll(
            Enumerable.Range(0, 12).Select(_ => processor.ApplyVerifiedAsync(payload, raw, CancellationToken.None)));

        Assert.Equal(1, outcomes.Count(x => x == MockWebhookDisposition.Applied));
        Assert.Equal(11, outcomes.Count(x => x == MockWebhookDisposition.Duplicate));

        await using var db = await source.OpenConnectionAsync();
        await using var check = new NpgsqlCommand("""
            SELECT count(*) FROM integration.mock_webhook_inbox
            WHERE provider_id='MOCK' AND event_id=@event
            """, db);
        check.Parameters.AddWithValue("event", payload.EventId);
        Assert.Equal(1L, (long)(await check.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task ContradictoryLaterEvent_IsRecordedButCannotRegressSucceeded()
    {
        if (string.IsNullOrWhiteSpace(Pg)) return;
        await using var source = NpgsqlDataSource.Create(Pg);
        var (created, original) = await NewPayloadAsync(source, "Succeeded");
        var processor = new NpgsqlMockWebhookProcessor(source);
        Assert.Equal(MockWebhookDisposition.Applied,
            await processor.ApplyVerifiedAsync(original,
                Encoding.UTF8.GetBytes(JsonSerializer.Serialize(original)), CancellationToken.None));

        var conflicting = original with { EventId = Guid.NewGuid(), Status = "Failed" };
        Assert.Equal(MockWebhookDisposition.ConflictingFinal,
            await processor.ApplyVerifiedAsync(conflicting,
                Encoding.UTF8.GetBytes(JsonSerializer.Serialize(conflicting)), CancellationToken.None));

        var payment = await new PaymentService(new NpgsqlPaymentStore(source)).GetAsync(
            created.Payment.MerchantId, created.Payment.PaymentId, CancellationToken.None);
        Assert.Equal(PaymentState.Succeeded, payment!.State);

        await using var db = await source.OpenConnectionAsync();
        await using var get = new NpgsqlCommand("""
            SELECT disposition FROM integration.mock_webhook_inbox
            WHERE provider_id='MOCK' AND event_id=@event
            """, db);
        get.Parameters.AddWithValue("event", conflicting.EventId);
        Assert.Equal("ConflictingFinal", (string)(await get.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task WrongAmountOrProviderKey_CannotChangePayment()
    {
        if (string.IsNullOrWhiteSpace(Pg)) return;
        await using var source = NpgsqlDataSource.Create(Pg);
        var (created, original) = await NewPayloadAsync(source, "Succeeded");
        var processor = new NpgsqlMockWebhookProcessor(source);

        var badAmount = original with { AmountMinor = original.AmountMinor + 1 };
        Assert.Equal(MockWebhookDisposition.InvalidCorrelation,
            await processor.ApplyVerifiedAsync(badAmount,
                Encoding.UTF8.GetBytes(JsonSerializer.Serialize(badAmount)), CancellationToken.None));

        var badKey = original with { ProviderOperationKey = "a-different-psp-operation" };
        Assert.Equal(MockWebhookDisposition.InvalidCorrelation,
            await processor.ApplyVerifiedAsync(badKey,
                Encoding.UTF8.GetBytes(JsonSerializer.Serialize(badKey)), CancellationToken.None));

        var payment = await new PaymentService(new NpgsqlPaymentStore(source)).GetAsync(
            created.Payment.MerchantId, created.Payment.PaymentId, CancellationToken.None);
        Assert.Equal(PaymentState.Pending, payment!.State);
    }

    [Fact]
    public async Task MockReconciliation_RecordsMismatchWithoutOverwritingPayment()
    {
        if (string.IsNullOrWhiteSpace(Pg)) return;
        await using var source = NpgsqlDataSource.Create(Pg);
        var (created, payload) = await NewPayloadAsync(source, "Succeeded");
        var provider = new NpgsqlMockPspLedger(source);
        Assert.Equal(PaymentState.Unknown,
            await provider.InitiateAsync(payload.ProviderOperationKey,
                MockPspBehavior.TimeoutAfterSuccess, CancellationToken.None));

        var reconciliation = new NpgsqlMockReconciliationStore(source);
        var mismatch = await reconciliation.ReconcileOneAsync(CancellationToken.None);
        Assert.NotNull(mismatch);
        Assert.Equal(created.Payment.PaymentId, mismatch!.PaymentId);
        Assert.Equal("Succeeded", mismatch.ProviderOutcome);
        Assert.Equal("Pending", mismatch.GatewayOutcome);

        var before = await new PaymentService(new NpgsqlPaymentStore(source)).GetAsync(
            created.Payment.MerchantId, created.Payment.PaymentId, CancellationToken.None);
        Assert.Equal(PaymentState.Pending, before!.State);

        var processor = new NpgsqlMockWebhookProcessor(source);
        Assert.Equal(MockWebhookDisposition.Applied,
            await processor.ApplyVerifiedAsync(payload,
                Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)), CancellationToken.None));

        Assert.True(await reconciliation.CloseResolvedAsync(CancellationToken.None) >= 1);
        await using var db = await source.OpenConnectionAsync();
        await using var q = new NpgsqlCommand("""
            SELECT resolved_at IS NOT NULL FROM reconciliation.mock_discrepancies
            WHERE provider_operation_key=@key
            """, db);
        q.Parameters.AddWithValue("key", payload.ProviderOperationKey);
        Assert.True((bool)(await q.ExecuteScalarAsync())!);
    }

    private static async Task<(CreationResult Payment, MockSignedWebhook Payload)> NewPayloadAsync(
        NpgsqlDataSource source, string status)
    {
        var service = new PaymentService(new NpgsqlPaymentStore(source));
        var created = await service.CreateAsync(new CreatePaymentCommand(
            Guid.NewGuid(), "webhook-" + Guid.NewGuid().ToString("N"),
            "signed-webhook-demo", 123400, "VND", "MOCK"), CancellationToken.None);
        await using var conn = await source.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT attempt_id, provider_operation_key
            FROM payment.payment_attempts WHERE payment_id=@payment
            """, conn);
        cmd.Parameters.AddWithValue("payment", created.Payment.PaymentId);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var attemptId = reader.GetGuid(0);
        var key = reader.GetString(1);
        var payload = new MockSignedWebhook(Guid.NewGuid(), created.Payment.PaymentId,
            attemptId, key, created.Payment.AmountMinor, created.Payment.Currency, status);
        return (created, payload);
    }
}
