using Npgsql;
using NpgsqlTypes;
using PaymentGateway.Refunds.Domain;

namespace PaymentGateway.Refunds.Infrastructure;

public sealed record UnknownRefundClaim(Guid RefundId,Guid PaymentId,Guid Token,string ProviderKey);

public sealed class NpgsqlRefundRecovery(NpgsqlDataSource source,NpgsqlMockRefundProvider provider)
{
    public async Task<UnknownRefundClaim?> ClaimOneAsync(Guid? onlyRefund,CancellationToken ct)
    {
        await using var conn=await source.OpenConnectionAsync(ct);
        await using var cmd=new NpgsqlCommand("""
            WITH picked AS (
              SELECT refund_id FROM refund.refund_requests
              WHERE state='Unknown' AND inquiry_count<5
                AND (@refund IS NULL OR refund_id=@refund)
                AND (next_inquiry_at IS NULL OR next_inquiry_at<=now())
              ORDER BY updated_at,refund_id LIMIT 1 FOR UPDATE SKIP LOCKED
            )
            UPDATE refund.refund_requests r
            SET inquiry_token=@token, inquiry_count=inquiry_count+1,
                next_inquiry_at=now()+interval '30 seconds',updated_at=now()
            FROM picked p WHERE r.refund_id=p.refund_id
            RETURNING r.refund_id,r.payment_id,r.provider_operation_key
            """,conn);
        var token=Guid.NewGuid();
        cmd.Parameters.AddWithValue("token",token);
        cmd.Parameters.AddWithValue("refund",NpgsqlDbType.Uuid,(object?)onlyRefund??DBNull.Value);
        await using var rd=await cmd.ExecuteReaderAsync(ct);
        if(!await rd.ReadAsync(ct))return null;
        return new UnknownRefundClaim(rd.GetGuid(0),rd.GetGuid(1),token,rd.GetString(2));
    }

    public async Task<RefundRecoveryResult> RecoverOneAsync(Guid? refundId,CancellationToken ct)
    {
        var claimed=await ClaimOneAsync(refundId,ct);
        if(claimed is null)return RefundRecoveryResult.NoWork;
        var verified=await provider.QueryAsync(claimed.ProviderKey,ct);
        if(verified is null)return RefundRecoveryResult.Unresolved;
        if(verified is not (RefundState.Succeeded or RefundState.Failed))
            throw new InvalidOperationException("Unexpected mock PSP refund status.");

        await using var conn=await source.OpenConnectionAsync(ct);
        await using var tx=await conn.BeginTransactionAsync(ct);
        long amount;
        await using(var update=new NpgsqlCommand("""
            UPDATE refund.refund_requests SET state=@state,inquiry_token=NULL,
                next_inquiry_at=NULL,updated_at=now()
            WHERE refund_id=@refund AND payment_id=@payment
              AND state='Unknown' AND inquiry_token=@token
            RETURNING amount_minor
            """,conn,tx))
        {
            update.Parameters.AddWithValue("state",verified.Value.ToString());
            update.Parameters.AddWithValue("refund",claimed.RefundId);
            update.Parameters.AddWithValue("payment",claimed.PaymentId);
            update.Parameters.AddWithValue("token",claimed.Token);
            var v=await update.ExecuteScalarAsync(ct);
            if(v is null or DBNull){await tx.RollbackAsync(ct);return RefundRecoveryResult.StaleClaim;}
            amount=(long)v;
        }
        await NpgsqlRefundProcessing.ApplyBudgetAsync(conn,tx,claimed.PaymentId,amount,verified.Value,ct);
        await RefundOutbox.AppendAsync(conn,tx,"Refund"+verified,claimed.RefundId,
            new{refund_id=claimed.RefundId,payment_id=claimed.PaymentId,
                evidence="MockRefundStatusInquiry",simulated=true},ct);
        await tx.CommitAsync(ct);
        return verified==RefundState.Succeeded?RefundRecoveryResult.VerifiedSuccess:RefundRecoveryResult.VerifiedFailure;
    }
}

public enum RefundRecoveryResult { NoWork, Unresolved, VerifiedSuccess, VerifiedFailure, StaleClaim }
