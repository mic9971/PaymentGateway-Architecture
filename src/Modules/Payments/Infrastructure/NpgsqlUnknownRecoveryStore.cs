using Npgsql;
using NpgsqlTypes;
using PaymentGateway.Payments.Domain;

namespace PaymentGateway.Payments.Infrastructure;

public sealed record ClaimedUnknownAttempt(
    Guid AttemptId, Guid PaymentId, string ProviderOperationKey, Guid InquiryToken);

/// <summary>
/// Inquiry leases only schedule read-only PSP status queries. They never reissue
/// a payment attempt. Concurrent resolvers can advance an Unknown state once.
/// </summary>
public sealed class NpgsqlUnknownRecoveryStore(NpgsqlDataSource dataSource)
{
    public Task<ClaimedUnknownAttempt?> ClaimOneAsync(CancellationToken ct)
        => ClaimAsync(null, ct);

    // Targeted diagnostic operation, intentionally not exposed as public merchant HTTP API.
    public Task<ClaimedUnknownAttempt?> ClaimPaymentAsync(Guid paymentId, CancellationToken ct)
        => ClaimAsync(paymentId, ct);

    private async Task<ClaimedUnknownAttempt?> ClaimAsync(Guid? paymentId, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            WITH candidate AS (
                SELECT attempt_id FROM payment.payment_attempts
                WHERE state='Unknown' AND inquiry_count < 5
                  AND (@payment_filter IS NULL OR payment_id=@payment_filter)
                  AND (next_inquiry_at IS NULL OR next_inquiry_at <= now())
                ORDER BY updated_at, attempt_id
                LIMIT 1 FOR UPDATE SKIP LOCKED
            )
            UPDATE payment.payment_attempts a
            SET inquiry_token=@token, next_inquiry_at=now() + interval '30 seconds',
                inquiry_count=inquiry_count+1, updated_at=now()
            FROM candidate c WHERE a.attempt_id=c.attempt_id
            RETURNING a.attempt_id, a.payment_id, a.provider_operation_key
            """, conn);
        var token = Guid.NewGuid();
        cmd.Parameters.AddWithValue("token", token);
        cmd.Parameters.AddWithValue("payment_filter", NpgsqlDbType.Uuid, (object?)paymentId ?? DBNull.Value);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new ClaimedUnknownAttempt(
            reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), token);
    }

    public async Task<bool> ResolveAsync(
        ClaimedUnknownAttempt attempt, PaymentState verifiedOutcome, CancellationToken ct)
    {
        if (verifiedOutcome is not (PaymentState.Succeeded or PaymentState.Failed))
            throw new ArgumentException("Only verified definitive outcomes resolve Unknown.");

        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var updateAttempt = new NpgsqlCommand("""
            UPDATE payment.payment_attempts
            SET state=@next, inquiry_token=NULL, next_inquiry_at=NULL, updated_at=now()
            WHERE attempt_id=@attempt AND payment_id=@payment AND state='Unknown'
              AND inquiry_token=@token
            RETURNING attempt_id
            """, conn, tx);
        updateAttempt.Parameters.AddWithValue("next", verifiedOutcome.ToString());
        updateAttempt.Parameters.AddWithValue("attempt", attempt.AttemptId);
        updateAttempt.Parameters.AddWithValue("payment", attempt.PaymentId);
        updateAttempt.Parameters.AddWithValue("token", attempt.InquiryToken);
        var applied = await updateAttempt.ExecuteScalarAsync(ct);
        if (applied is null or DBNull)
        {
            await tx.RollbackAsync(ct);
            return false;
        }

        await using var updatePayment = new NpgsqlCommand("""
            UPDATE payment.payment_intents
            SET state=@next, version=version+1, updated_at=now()
            WHERE payment_id=@payment AND state='Unknown'
            """, conn, tx);
        updatePayment.Parameters.AddWithValue("next", verifiedOutcome.ToString());
        updatePayment.Parameters.AddWithValue("payment", attempt.PaymentId);
        if (await updatePayment.ExecuteNonQueryAsync(ct) != 1)
            throw new InvalidOperationException("Unknown reconciliation must not overwrite another state.");

        await OutboxWriter.AppendAsync(conn, tx, "Payment" + verifiedOutcome, attempt.PaymentId,
            new { payment_id = attempt.PaymentId, attempt_id = attempt.AttemptId,
                  evidence = "MockPspStatusInquiry", simulated = true }, ct);
        await tx.CommitAsync(ct);
        return true;
    }
}

public enum RecoveryResult { NoWork, Unresolved, VerifiedSuccess, VerifiedFailure, StaleClaim }

/// <summary>No calls to PSP CreatePayment, only read-only status inquiry.</summary>
public sealed class PaymentRecoveryService(NpgsqlUnknownRecoveryStore store, NpgsqlMockPspLedger provider)
{
    public async Task<RecoveryResult> RecoverOneAsync(CancellationToken ct)
        => await ResolveClaimAsync(await store.ClaimOneAsync(ct), ct);

    public async Task<RecoveryResult> RecoverPaymentAsync(Guid paymentId, CancellationToken ct)
        => await ResolveClaimAsync(await store.ClaimPaymentAsync(paymentId, ct), ct);

    private async Task<RecoveryResult> ResolveClaimAsync(ClaimedUnknownAttempt? claimed, CancellationToken ct)
    {
        if (claimed is null) return RecoveryResult.NoWork;

        var verified = await provider.QueryAsync(claimed.ProviderOperationKey, ct);
        if (verified is null) return RecoveryResult.Unresolved;
        if (verified is not (PaymentState.Succeeded or PaymentState.Failed))
            throw new InvalidOperationException("Untrusted mock PSP outcome.");
        if (!await store.ResolveAsync(claimed, verified.Value, ct))
            return RecoveryResult.StaleClaim;
        return verified is PaymentState.Succeeded
            ? RecoveryResult.VerifiedSuccess
            : RecoveryResult.VerifiedFailure;
    }
}
