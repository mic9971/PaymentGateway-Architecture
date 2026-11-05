using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using PaymentGateway.Payments.Application;
using PaymentGateway.Payments.Domain;

namespace PaymentGateway.Payments.Infrastructure;

/// <summary>All create-payment records (including outbox) are committed in a single local DB transaction.</summary>
public sealed class NpgsqlPaymentStore(NpgsqlDataSource dataSource) : IPaymentStore
{
    public async Task<CreationResult> CreateOrGetAsync(
        CreatePaymentCommand request, byte[] requestHash, CancellationToken ct)
    {
        var newPaymentId = Guid.NewGuid();
        var newAttemptId = Guid.NewGuid();
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        // A deferrable FK lets the unique idempotency claim precede its Payment row IN ONE transaction.
        await using var claim = new NpgsqlCommand("""
            INSERT INTO payment.idempotency_records
              (merchant_id, operation, idempotency_key, request_hash, payment_id)
            VALUES (@merchant, 'CreatePayment', @key, @hash, @payment)
            ON CONFLICT DO NOTHING RETURNING payment_id
            """, connection, tx);
        claim.Parameters.AddWithValue("merchant", request.AuthenticatedMerchantId);
        claim.Parameters.AddWithValue("key", request.IdempotencyKey);
        claim.Parameters.AddWithValue("hash", NpgsqlDbType.Bytea, requestHash);
        claim.Parameters.AddWithValue("payment", newPaymentId);
        var claimed = await claim.ExecuteScalarAsync(ct);

        if (claimed is null or DBNull)
        {
            await using var existing = new NpgsqlCommand("""
                SELECT request_hash, payment_id FROM payment.idempotency_records
                WHERE merchant_id = @merchant AND operation = 'CreatePayment' AND idempotency_key = @key
                """, connection, tx);
            existing.Parameters.AddWithValue("merchant", request.AuthenticatedMerchantId);
            existing.Parameters.AddWithValue("key", request.IdempotencyKey);
            await using var reader = await existing.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                throw new InvalidOperationException("Idempotency conflict row not visible; retry same key.");
            var priorHash = reader.GetFieldValue<byte[]>(0);
            var priorPaymentId = reader.GetGuid(1);
            await reader.DisposeAsync();
            if (!priorHash.AsSpan().SequenceEqual(requestHash))
                throw new IdempotencyConflictException();

            var snapshot = await ReadAsync(connection, tx, request.AuthenticatedMerchantId, priorPaymentId, ct)
                ?? throw new InvalidOperationException("Committed idempotency row has no payment.");
            await tx.CommitAsync(ct);
            return new CreationResult(snapshot, Replayed: true);
        }

        await using (var insertPayment = new NpgsqlCommand("""
            INSERT INTO payment.payment_intents
                (payment_id, merchant_id, merchant_reference, amount_minor, currency, payment_method, state)
            VALUES (@id, @merchant, @reference, @amount, @currency, @method, 'Pending')
            """, connection, tx))
        {
            insertPayment.Parameters.AddWithValue("id", newPaymentId);
            insertPayment.Parameters.AddWithValue("merchant", request.AuthenticatedMerchantId);
            insertPayment.Parameters.AddWithValue("reference", request.MerchantReference);
            insertPayment.Parameters.AddWithValue("amount", request.AmountMinor);
            insertPayment.Parameters.AddWithValue("currency", request.Currency);
            insertPayment.Parameters.AddWithValue("method", request.PaymentMethod);
            await insertPayment.ExecuteNonQueryAsync(ct);
        }

        await using (var attempt = new NpgsqlCommand("""
            INSERT INTO payment.payment_attempts
              (attempt_id, payment_id, provider_id, provider_operation_key, state)
            VALUES (@attempt, @payment, 'MOCK', @key, 'Pending')
            """, connection, tx))
        {
            attempt.Parameters.AddWithValue("attempt", newAttemptId);
            attempt.Parameters.AddWithValue("payment", newPaymentId);
            attempt.Parameters.AddWithValue("key", "demo-" + newAttemptId.ToString("N"));
            await attempt.ExecuteNonQueryAsync(ct);
        }

        await OutboxWriter.AppendAsync(connection, tx, "PaymentRequested", newPaymentId,
            new { payment_id = newPaymentId, merchant_id = request.AuthenticatedMerchantId }, ct);
        // The unique FK and payment row must become visible together or not at all.
        await tx.CommitAsync(ct);
        var result = await GetAsync(request.AuthenticatedMerchantId, newPaymentId, ct)
            ?? throw new InvalidOperationException("New payment was not found after commit.");
        return new CreationResult(result, Replayed: false);
    }

    public async Task<PaymentSnapshot?> GetAsync(Guid merchantId, Guid paymentId, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        return await ReadAsync(connection, null, merchantId, paymentId, ct);
    }

    private static async Task<PaymentSnapshot?> ReadAsync(
        NpgsqlConnection connection, NpgsqlTransaction? tx, Guid merchantId, Guid paymentId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT payment_id, merchant_id, merchant_reference, amount_minor,
                   currency, payment_method, state, version, created_at
            FROM payment.payment_intents
            WHERE payment_id = @id AND merchant_id = @merchant
            """, connection, tx);
        cmd.Parameters.AddWithValue("id", paymentId);
        cmd.Parameters.AddWithValue("merchant", merchantId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new PaymentSnapshot(
            reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2),
            reader.GetInt64(3), reader.GetString(4), reader.GetString(5),
            Enum.Parse<PaymentState>(reader.GetString(6)),
            reader.GetInt64(7), reader.GetFieldValue<DateTimeOffset>(8));
    }
}

internal static class OutboxWriter
{
    public static async Task AppendAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string eventType,
        Guid paymentId, object payload, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO messaging.outbox_messages
                (event_id, aggregate_id, event_type, payload, occurred_at)
            VALUES (@event, @aggregate, @type, @payload, now())
            """, conn, tx);
        cmd.Parameters.AddWithValue("event", Guid.NewGuid());
        cmd.Parameters.AddWithValue("aggregate", paymentId);
        cmd.Parameters.AddWithValue("type", eventType);
        cmd.Parameters.AddWithValue("payload", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(payload));
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
