using Npgsql;
using PaymentGateway.Payments.Domain;

namespace PaymentGateway.Payments.Infrastructure;

public sealed record ClaimedAttempt(
    Guid AttemptId, Guid PaymentId, Guid ClaimId, string ProviderOperationKey);

/// <summary>Demo worker claims with PostgreSQL locks; a timed-out claim is UNKNOWN, not auto-retried.</summary>
public sealed class NpgsqlPaymentWorkStore(NpgsqlDataSource dataSource)
{
    public Task<ClaimedAttempt?> ClaimPendingAsync(CancellationToken ct)
        => ClaimAsync(null,ct);

    // Allows isolated test/operator claim; normal worker still claims the global queue.
    public Task<ClaimedAttempt?> ClaimPaymentAsync(Guid paymentId,CancellationToken ct)
        => ClaimAsync(paymentId,ct);

    private async Task<ClaimedAttempt?> ClaimAsync(Guid? targetPaymentId,CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var claimId = Guid.NewGuid();
        await using var claim = new NpgsqlCommand("""
            WITH candidate AS (
                SELECT attempt_id FROM payment.payment_attempts
                WHERE state = 'Pending'
                  AND (@payment_filter IS NULL OR payment_id=@payment_filter)
                ORDER BY created_at, attempt_id
                LIMIT 1 FOR UPDATE SKIP LOCKED
            )
            UPDATE payment.payment_attempts a
            SET state = 'Processing', claim_id = @claim, lease_until = now() + interval '30 seconds',
                updated_at = now()
            FROM candidate c
            WHERE a.attempt_id = c.attempt_id
            RETURNING a.attempt_id, a.payment_id, a.provider_operation_key
            """, conn, tx);
        claim.Parameters.AddWithValue("claim", claimId);
        claim.Parameters.AddWithValue("payment_filter",NpgsqlTypes.NpgsqlDbType.Uuid,
            (object?)targetPaymentId ?? DBNull.Value);
        Guid attemptId;
        Guid paymentId;
        string providerKey;
        await using (var reader = await claim.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct))
            {
                await tx.CommitAsync(ct);
                return null;
            }
            attemptId = reader.GetGuid(0);
            paymentId = reader.GetGuid(1);
            providerKey = reader.GetString(2);
        }
        await using var mark = new NpgsqlCommand("""
            UPDATE payment.payment_intents SET state='Processing', version=version+1, updated_at=now()
            WHERE payment_id=@id AND state='Pending'
            """, conn, tx);
        mark.Parameters.AddWithValue("id", paymentId);
        if (await mark.ExecuteNonQueryAsync(ct) != 1)
            throw new InvalidOperationException("Payment state changed while claiming attempt.");
        await tx.CommitAsync(ct);
        return new ClaimedAttempt(attemptId, paymentId, claimId, providerKey);
    }

    public async Task<bool> CompleteAsync(ClaimedAttempt work, PaymentState outcome, CancellationToken ct)
    {
        if (outcome is not (PaymentState.Succeeded or PaymentState.Failed or PaymentState.Unknown))
            throw new ArgumentOutOfRangeException(nameof(outcome));

        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var updateAttempt = new NpgsqlCommand("""
            UPDATE payment.payment_attempts
            SET state=@state, lease_until=NULL, updated_at=now()
            WHERE attempt_id=@attempt AND claim_id=@claim AND state='Processing'
            RETURNING payment_id
            """, conn, tx);
        updateAttempt.Parameters.AddWithValue("state", outcome.ToString());
        updateAttempt.Parameters.AddWithValue("attempt", work.AttemptId);
        updateAttempt.Parameters.AddWithValue("claim", work.ClaimId);
        var paymentId = await updateAttempt.ExecuteScalarAsync(ct);
        if (paymentId is not Guid)
        {
            await tx.RollbackAsync(ct);
            return false;
        }
        await using var updatePayment = new NpgsqlCommand("""
            UPDATE payment.payment_intents
            SET state=@state, version=version+1, updated_at=now()
            WHERE payment_id=@id AND state='Processing'
            """, conn, tx);
        updatePayment.Parameters.AddWithValue("state", outcome.ToString());
        updatePayment.Parameters.AddWithValue("id", work.PaymentId);
        if (await updatePayment.ExecuteNonQueryAsync(ct) != 1)
            throw new InvalidOperationException("Payment state drift while completing a claimed PSP attempt.");

        await OutboxWriter.AppendAsync(conn, tx, "Payment" + outcome, work.PaymentId,
            new { payment_id = work.PaymentId, attempt_id = work.AttemptId,
                result = outcome.ToString(), provider = "MOCK", simulated = true }, ct);
        await tx.CommitAsync(ct);
        return true;
    }

    /// <summary>
    /// A crashed/expired worker may have contacted PSP. Never auto-reissue its side effect.
    /// Mark UNKNOWN and leave manual/inquiry recovery to a subsequent, separately reviewed slice.
    /// </summary>
    public async Task<bool> MarkOneExpiredClaimUnknownAsync(CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var claim = new NpgsqlCommand("""
            SELECT attempt_id, payment_id FROM payment.payment_attempts
            WHERE state='Processing' AND lease_until < now()
            ORDER BY lease_until, attempt_id LIMIT 1 FOR UPDATE SKIP LOCKED
            """, conn, tx);
        Guid attemptId, paymentId;
        await using (var reader = await claim.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct))
            {
                await tx.CommitAsync(ct);
                return false;
            }
            attemptId = reader.GetGuid(0);
            paymentId = reader.GetGuid(1);
        }
        await using var expire = new NpgsqlCommand("""
            UPDATE payment.payment_attempts
            SET state='Unknown', claim_id=NULL, lease_until=NULL, updated_at=now()
            WHERE attempt_id=@id AND state='Processing'
            """, conn, tx);
        expire.Parameters.AddWithValue("id", attemptId);
        if (await expire.ExecuteNonQueryAsync(ct) != 1)
            throw new InvalidOperationException("Expected locked expired attempt.");
        await using var payment = new NpgsqlCommand("""
            UPDATE payment.payment_intents
            SET state='Unknown', version=version+1, updated_at=now()
            WHERE payment_id=@id AND state='Processing'
            """, conn, tx);
        payment.Parameters.AddWithValue("id", paymentId);
        if (await payment.ExecuteNonQueryAsync(ct) != 1)
            throw new InvalidOperationException("Payment state drift during attempt timeout.");
        await OutboxWriter.AppendAsync(conn, tx, "PaymentUnknown", paymentId,
            new { payment_id = paymentId, attempt_id = attemptId,
                reason = "WorkerLeaseExpired", simulated = true }, ct);
        await tx.CommitAsync(ct);
        return true;
    }
}
