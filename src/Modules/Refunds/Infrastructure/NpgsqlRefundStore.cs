using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using PaymentGateway.Refunds.Application;
using PaymentGateway.Refunds.Domain;

namespace PaymentGateway.Refunds.Infrastructure;

/// <summary>
/// One local DB transaction: verified Payment read-lock, idempotent Refund request,
/// atomically reserved RefundBudget, and RefundRequested Outbox message.
/// This intentionally couples payment and refund rows in one DB deployment (ADR-0013 A).
/// </summary>
public sealed class NpgsqlRefundStore(NpgsqlDataSource dataSource) : IRefundStore
{
    public async Task<RefundCreationResult> CreateOrGetAsync(
        RequestRefundCommand command, byte[] fingerprint, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        long verifiedLimit;
        string currency;
        string paymentState;
        await using (var payment = new NpgsqlCommand("""
            SELECT amount_minor,currency,state FROM payment.payment_intents
            WHERE payment_id=@payment AND merchant_id=@merchant FOR UPDATE
            """, conn, tx))
        {
            payment.Parameters.AddWithValue("payment", command.PaymentId);
            payment.Parameters.AddWithValue("merchant", command.AuthenticatedMerchantId);
            await using var rd = await payment.ExecuteReaderAsync(ct);
            if (!await rd.ReadAsync(ct)) throw new PaymentNotRefundableException();
            verifiedLimit = rd.GetInt64(0);
            currency = rd.GetString(1);
            paymentState = rd.GetString(2);
        }
        if (paymentState != "Succeeded" || currency != "VND")
            throw new PaymentNotRefundableException();

        var refundId = Guid.NewGuid();
        var providerKey = "demo-refund-" + refundId.ToString("N");
        await using var claim = new NpgsqlCommand("""
            INSERT INTO refund.refund_requests
              (refund_id,payment_id,merchant_id,amount_minor,currency,
               idempotency_key,request_hash,provider_operation_key,state)
            VALUES (@refund,@payment,@merchant,@amount,@currency,
                    @key,@hash,@provider,'Requested')
            ON CONFLICT (merchant_id,idempotency_key) DO NOTHING
            RETURNING refund_id
            """, conn, tx);
        claim.Parameters.AddWithValue("refund", refundId);
        claim.Parameters.AddWithValue("payment", command.PaymentId);
        claim.Parameters.AddWithValue("merchant", command.AuthenticatedMerchantId);
        claim.Parameters.AddWithValue("amount", command.AmountMinor);
        claim.Parameters.AddWithValue("currency", currency);
        claim.Parameters.AddWithValue("key", command.IdempotencyKey);
        claim.Parameters.AddWithValue("hash", NpgsqlDbType.Bytea, fingerprint);
        claim.Parameters.AddWithValue("provider", providerKey);
        var inserted = await claim.ExecuteScalarAsync(ct);
        if (inserted is null or DBNull)
        {
            Guid existingRefundId;
            byte[] oldHash;
            await using (var existing = new NpgsqlCommand("""
                SELECT refund_id,request_hash FROM refund.refund_requests
                WHERE merchant_id=@merchant AND idempotency_key=@key
                """, conn, tx))
            {
                existing.Parameters.AddWithValue("merchant", command.AuthenticatedMerchantId);
                existing.Parameters.AddWithValue("key", command.IdempotencyKey);
                await using var rd = await existing.ExecuteReaderAsync(ct);
                if (!await rd.ReadAsync(ct))
                    throw new InvalidOperationException("Idempotency conflict row not visible.");
                existingRefundId = rd.GetGuid(0);
                oldHash = rd.GetFieldValue<byte[]>(1);
            }
            if (!oldHash.AsSpan().SequenceEqual(fingerprint))
                throw new RefundConflictException("Idempotency-Key reused with different refund details.");
            var existingSnapshot = await ReadAsync(conn, tx, command.AuthenticatedMerchantId, existingRefundId, ct)
                ?? throw new InvalidOperationException("Idempotency row references missing refund.");
            await tx.CommitAsync(ct);
            return new RefundCreationResult(existingSnapshot, true);
        }

        await using (var init = new NpgsqlCommand("""
            INSERT INTO refund.refund_budgets(payment_id,verified_limit_minor)
            VALUES (@payment,@limit) ON CONFLICT (payment_id) DO NOTHING
            """, conn, tx))
        {
            init.Parameters.AddWithValue("payment", command.PaymentId);
            init.Parameters.AddWithValue("limit", verifiedLimit);
            await init.ExecuteNonQueryAsync(ct);
        }

        await using (var reserve = new NpgsqlCommand("""
            UPDATE refund.refund_budgets
            SET reserved_minor=reserved_minor+@amount
            WHERE payment_id=@payment
              AND reserved_minor + completed_minor + @amount <= verified_limit_minor
            RETURNING payment_id
            """, conn, tx))
        {
            reserve.Parameters.AddWithValue("amount", command.AmountMinor);
            reserve.Parameters.AddWithValue("payment", command.PaymentId);
            var reserved = await reserve.ExecuteScalarAsync(ct);
            if (reserved is null or DBNull)
                throw new InsufficientRefundBudgetException();
        }

        await RefundOutbox.AppendAsync(conn, tx, "RefundRequested", refundId,
            new { refund_id = refundId, payment_id = command.PaymentId,
                simulated = true }, ct);
        await tx.CommitAsync(ct);
        var snapshot = await GetAsync(command.AuthenticatedMerchantId, refundId, ct)
            ?? throw new InvalidOperationException("Committed refund missing.");
        return new RefundCreationResult(snapshot, false);
    }

    public async Task<RefundSnapshot?> GetAsync(Guid merchantId, Guid refundId, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        return await ReadAsync(conn, null, merchantId, refundId, ct);
    }

    private static async Task<RefundSnapshot?> ReadAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx, Guid merchantId, Guid refundId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT refund_id,payment_id,merchant_id,amount_minor,currency,state,created_at
            FROM refund.refund_requests
            WHERE refund_id=@refund AND merchant_id=@merchant
            """, conn, tx);
        cmd.Parameters.AddWithValue("refund", refundId);
        cmd.Parameters.AddWithValue("merchant", merchantId);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        if (!await rd.ReadAsync(ct)) return null;
        return new RefundSnapshot(rd.GetGuid(0),rd.GetGuid(1),rd.GetGuid(2),
            rd.GetInt64(3),rd.GetString(4),Enum.Parse<RefundState>(rd.GetString(5)),
            rd.GetFieldValue<DateTimeOffset>(6));
    }
}

internal static class RefundOutbox
{
    public static async Task AppendAsync(
        NpgsqlConnection conn,NpgsqlTransaction tx,string kind,
        Guid aggregateId,object payload,CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO messaging.outbox_messages(event_id,aggregate_id,event_type,payload,occurred_at)
            VALUES (@event,@aggregate,@type,@payload,now())
            """,conn,tx);
        cmd.Parameters.AddWithValue("event",Guid.NewGuid());
        cmd.Parameters.AddWithValue("aggregate",aggregateId);
        cmd.Parameters.AddWithValue("type",kind);
        cmd.Parameters.AddWithValue("payload",NpgsqlDbType.Jsonb,JsonSerializer.Serialize(payload));
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
