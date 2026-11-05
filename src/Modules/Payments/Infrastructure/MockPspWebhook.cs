using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Npgsql;
using NpgsqlTypes;
using PaymentGateway.Payments.Domain;

namespace PaymentGateway.Payments.Infrastructure;

public sealed record MockSignedWebhook(
    [property: JsonPropertyName("event_id")] Guid EventId,
    [property: JsonPropertyName("payment_id")] Guid PaymentId,
    [property: JsonPropertyName("attempt_id")] Guid AttemptId,
    [property: JsonPropertyName("provider_operation_key")] string ProviderOperationKey,
    [property: JsonPropertyName("amount_minor")] long AmountMinor,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("status")] string Status);

public static class MockWebhookSignature
{
    /// <summary>Demo-only protocol: lower-case hex SHA256 HMAC over timestamp + "." + raw JSON bytes.</summary>
    public static string Sign(byte[] secret, string timestamp, ReadOnlySpan<byte> payload)
    {
        var prefix = Encoding.UTF8.GetBytes(timestamp + ".");
        var combined = new byte[prefix.Length + payload.Length];
        prefix.CopyTo(combined, 0);
        payload.CopyTo(combined.AsSpan(prefix.Length));
        return "sha256=" + Convert.ToHexString(HMACSHA256.HashData(secret, combined)).ToLowerInvariant();
    }

    public static bool Verify(
        byte[] secret, string timestamp, string supplied,
        ReadOnlySpan<byte> payload, DateTimeOffset now)
    {
        if (secret.Length < 16 || payload.Length is <= 0 or > 16_384
            || !long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            || !supplied.StartsWith("sha256=", StringComparison.Ordinal)
            || supplied.Length != 71) return false;
        DateTimeOffset signedTime;
        try { signedTime = DateTimeOffset.FromUnixTimeSeconds(seconds); }
        catch (ArgumentOutOfRangeException) { return false; }
        if (Math.Abs((now - signedTime).TotalSeconds) > 300) return false;
        byte[] actual;
        try { actual = Convert.FromHexString(supplied["sha256=".Length..]); }
        catch (FormatException) { return false; }
        var expectedText = Sign(secret, timestamp, payload)["sha256=".Length..];
        var expected = Convert.FromHexString(expectedText);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}

public enum MockWebhookDisposition
{
    Applied, Duplicate, AlreadyFinal, ConflictingFinal, InvalidCorrelation
}

public sealed class NpgsqlMockWebhookProcessor(NpgsqlDataSource dataSource)
{
    public async Task<MockWebhookDisposition> ApplyVerifiedAsync(
        MockSignedWebhook message, ReadOnlyMemory<byte> rawBody, CancellationToken ct)
    {
        if (message.EventId == Guid.Empty || message.PaymentId == Guid.Empty
            || message.AttemptId == Guid.Empty || string.IsNullOrWhiteSpace(message.ProviderOperationKey)
            || message.AmountMinor <= 0 || message.Currency != "VND"
            || message.Status is not ("Succeeded" or "Failed"))
            return MockWebhookDisposition.InvalidCorrelation;

        var digest = SHA256.HashData(rawBody.Span);
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        // Same lock order as the payment worker: Attempt, then Payment.
        Guid actualPayment;
        string providerKey, providerId, attemptState;
        await using (var getAttempt = new NpgsqlCommand("""
            SELECT payment_id,provider_operation_key,provider_id,state
            FROM payment.payment_attempts WHERE attempt_id=@attempt FOR UPDATE
            """, conn, tx))
        {
            getAttempt.Parameters.AddWithValue("attempt", message.AttemptId);
            await using var reader = await getAttempt.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return MockWebhookDisposition.InvalidCorrelation;
            actualPayment = reader.GetGuid(0);
            providerKey = reader.GetString(1);
            providerId = reader.GetString(2);
            attemptState = reader.GetString(3);
        }
        if (actualPayment != message.PaymentId || providerKey != message.ProviderOperationKey
            || providerId != "MOCK") return MockWebhookDisposition.InvalidCorrelation;

        string paymentState;
        long amount;
        string currency;
        await using (var getPayment = new NpgsqlCommand("""
            SELECT state,amount_minor,currency FROM payment.payment_intents
            WHERE payment_id=@payment FOR UPDATE
            """, conn, tx))
        {
            getPayment.Parameters.AddWithValue("payment", message.PaymentId);
            await using var reader = await getPayment.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return MockWebhookDisposition.InvalidCorrelation;
            paymentState = reader.GetString(0);
            amount = reader.GetInt64(1);
            currency = reader.GetString(2);
        }
        if (amount != message.AmountMinor || currency != message.Currency)
            return MockWebhookDisposition.InvalidCorrelation;

        await using var claim = new NpgsqlCommand("""
            INSERT INTO integration.mock_webhook_inbox
              (provider_id,event_id,payment_id,attempt_id,provider_operation_key,payload_hash,outcome,disposition)
            VALUES ('MOCK',@event,@payment,@attempt,@key,@hash,@outcome,'Received')
            ON CONFLICT (provider_id,event_id) DO NOTHING RETURNING event_id
            """, conn, tx);
        claim.Parameters.AddWithValue("event", message.EventId);
        claim.Parameters.AddWithValue("payment", message.PaymentId);
        claim.Parameters.AddWithValue("attempt", message.AttemptId);
        claim.Parameters.AddWithValue("key", message.ProviderOperationKey);
        claim.Parameters.AddWithValue("hash", NpgsqlDbType.Bytea, digest);
        claim.Parameters.AddWithValue("outcome", message.Status);
        var inserted = await claim.ExecuteScalarAsync(ct);
        if (inserted is null or DBNull)
        {
            await using var previous = new NpgsqlCommand("""
                SELECT payload_hash FROM integration.mock_webhook_inbox
                WHERE provider_id='MOCK' AND event_id=@event
                """, conn, tx);
            previous.Parameters.AddWithValue("event", message.EventId);
            var oldHash = (byte[])(await previous.ExecuteScalarAsync(ct)
                ?? throw new InvalidOperationException("Existing event identity disappeared."));
            if (!CryptographicOperations.FixedTimeEquals(digest, oldHash))
                return MockWebhookDisposition.InvalidCorrelation;
            await tx.CommitAsync(ct);
            return MockWebhookDisposition.Duplicate;
        }

        MockWebhookDisposition disposition;
        if (paymentState == message.Status)
        {
            disposition = MockWebhookDisposition.AlreadyFinal;
        }
        else if (paymentState is "Succeeded" or "Failed" || attemptState is "Succeeded" or "Failed")
        {
            disposition = MockWebhookDisposition.ConflictingFinal;
        }
        else if (paymentState is "Pending" or "Processing" or "RequiresAction" or "Unknown")
        {
            await using (var changeAttempt = new NpgsqlCommand("""
                UPDATE payment.payment_attempts
                SET state=@status, claim_id=NULL, lease_until=NULL,
                    inquiry_token=NULL, next_inquiry_at=NULL, updated_at=now()
                WHERE attempt_id=@attempt AND state IN ('Pending','Processing','Unknown')
                """, conn, tx))
            {
                changeAttempt.Parameters.AddWithValue("status", message.Status);
                changeAttempt.Parameters.AddWithValue("attempt", message.AttemptId);
                if (await changeAttempt.ExecuteNonQueryAsync(ct) != 1)
                    throw new InvalidOperationException("Unexpected attempt state for verified callback.");
            }
            await using (var changePayment = new NpgsqlCommand("""
                UPDATE payment.payment_intents SET state=@status,
                    version=version+1, updated_at=now()
                WHERE payment_id=@payment AND state IN ('Pending','Processing','RequiresAction','Unknown')
                """, conn, tx))
            {
                changePayment.Parameters.AddWithValue("status", message.Status);
                changePayment.Parameters.AddWithValue("payment", message.PaymentId);
                if (await changePayment.ExecuteNonQueryAsync(ct) != 1)
                    throw new InvalidOperationException("Concurrent payment transition.");
            }
            await OutboxWriter.AppendAsync(conn, tx, "Payment" + message.Status,
                message.PaymentId, new {
                    payment_id = message.PaymentId, attempt_id = message.AttemptId,
                    evidence = "MockSignedWebhook", provider_event_id = message.EventId,
                    simulated = true
                }, ct);
            disposition = MockWebhookDisposition.Applied;
        }
        else disposition = MockWebhookDisposition.ConflictingFinal;

        await using var dispositionUpdate = new NpgsqlCommand("""
            UPDATE integration.mock_webhook_inbox SET disposition=@disposition
            WHERE provider_id='MOCK' AND event_id=@event
            """, conn, tx);
        dispositionUpdate.Parameters.AddWithValue("disposition", disposition.ToString());
        dispositionUpdate.Parameters.AddWithValue("event", message.EventId);
        await dispositionUpdate.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
        return disposition;
    }

    public static MockSignedWebhook Deserialize(ReadOnlySpan<byte> body)
        => JsonSerializer.Deserialize<MockSignedWebhook>(body)
            ?? throw new JsonException("Missing webhook payload.");
}
