using Npgsql;
using PaymentGateway.Refunds.Domain;

namespace PaymentGateway.Refunds.Infrastructure;

public sealed record ClaimedRefund(Guid RefundId,Guid PaymentId,Guid ClaimToken,string ProviderKey);

/// <summary>DB coordinator only; provider calls happen after the claim transaction commits.</summary>
public sealed class NpgsqlRefundProcessing(NpgsqlDataSource source)
{
    public async Task<ClaimedRefund?> ClaimOneAsync(CancellationToken ct)
    {
        await using var conn=await source.OpenConnectionAsync(ct);
        await using var cmd=new NpgsqlCommand("""
            WITH candidate AS (
              SELECT refund_id FROM refund.refund_requests
              WHERE state='Requested' ORDER BY created_at,refund_id
              LIMIT 1 FOR UPDATE SKIP LOCKED
            )
            UPDATE refund.refund_requests r
            SET state='Processing',claim_id=@token,lease_until=now()+interval '30 seconds',updated_at=now()
            FROM candidate c WHERE r.refund_id=c.refund_id
            RETURNING r.refund_id,r.payment_id,r.provider_operation_key
            """,conn);
        var token=Guid.NewGuid();
        cmd.Parameters.AddWithValue("token",token);
        await using var rd=await cmd.ExecuteReaderAsync(ct);
        if(!await rd.ReadAsync(ct)) return null;
        return new ClaimedRefund(rd.GetGuid(0),rd.GetGuid(1),token,rd.GetString(2));
    }

    public async Task<bool> CompleteAsync(ClaimedRefund claimed,RefundState final,CancellationToken ct)
    {
        if(final is not (RefundState.Succeeded or RefundState.Failed or RefundState.Unknown))
            throw new ArgumentException("Only definitive or ambiguous PSP outcomes.");
        await using var conn=await source.OpenConnectionAsync(ct);
        await using var tx=await conn.BeginTransactionAsync(ct);
        long amount;
        await using (var change=new NpgsqlCommand("""
            UPDATE refund.refund_requests
            SET state=@next,claim_id=NULL,lease_until=NULL,updated_at=now()
            WHERE refund_id=@refund AND payment_id=@payment AND claim_id=@claim
              AND state='Processing'
            RETURNING amount_minor
            """,conn,tx))
        {
            change.Parameters.AddWithValue("next",final.ToString());
            change.Parameters.AddWithValue("refund",claimed.RefundId);
            change.Parameters.AddWithValue("payment",claimed.PaymentId);
            change.Parameters.AddWithValue("claim",claimed.ClaimToken);
            var v=await change.ExecuteScalarAsync(ct);
            if(v is null or DBNull){await tx.RollbackAsync(ct);return false;}
            amount=(long)v;
        }
        if(final is not RefundState.Unknown)
            await ApplyBudgetAsync(conn,tx,claimed.PaymentId,amount,final,ct);
        await RefundOutbox.AppendAsync(conn,tx,"Refund"+final,claimed.RefundId,
            new {refund_id=claimed.RefundId,payment_id=claimed.PaymentId,
                result=final.ToString(),simulated=true},ct);
        await tx.CommitAsync(ct);
        return true;
    }

    public async Task<bool> ExpireOneProcessingAsync(CancellationToken ct)
    {
        await using var conn=await source.OpenConnectionAsync(ct);
        await using var tx=await conn.BeginTransactionAsync(ct);
        Guid refundId,paymentId;
        await using(var pick=new NpgsqlCommand("""
            SELECT refund_id,payment_id FROM refund.refund_requests
            WHERE state='Processing' AND lease_until<now()
            ORDER BY lease_until,refund_id LIMIT 1 FOR UPDATE SKIP LOCKED
            """,conn,tx))
        {
            await using var rd=await pick.ExecuteReaderAsync(ct);
            if(!await rd.ReadAsync(ct)){await tx.CommitAsync(ct);return false;}
            refundId=rd.GetGuid(0);paymentId=rd.GetGuid(1);
        }
        await using(var update=new NpgsqlCommand("""
            UPDATE refund.refund_requests SET state='Unknown',claim_id=NULL,
                lease_until=NULL,updated_at=now()
            WHERE refund_id=@id AND state='Processing'
            """,conn,tx))
        {
            update.Parameters.AddWithValue("id",refundId);
            if(await update.ExecuteNonQueryAsync(ct)!=1) throw new InvalidOperationException("Lost refund claim.");
        }
        // Reservation must remain reserved until PSP outcome is verified.
        await RefundOutbox.AppendAsync(conn,tx,"RefundUnknown",refundId,
            new {refund_id=refundId,payment_id=paymentId,reason="ExpiredLease",simulated=true},ct);
        await tx.CommitAsync(ct);
        return true;
    }

    internal static async Task ApplyBudgetAsync(
        NpgsqlConnection conn,NpgsqlTransaction tx,Guid payment,long amount,
        RefundState final,CancellationToken ct)
    {
        await using var cmd=new NpgsqlCommand("""
            UPDATE refund.refund_budgets SET
                reserved_minor=reserved_minor-@amount,
                completed_minor=completed_minor+@completed
            WHERE payment_id=@payment AND reserved_minor>=@amount
            """,conn,tx);
        cmd.Parameters.AddWithValue("payment",payment);
        cmd.Parameters.AddWithValue("amount",amount);
        cmd.Parameters.AddWithValue("completed",final==RefundState.Succeeded?amount:0L);
        if(await cmd.ExecuteNonQueryAsync(ct)!=1)
            throw new InvalidOperationException("Refund budget state drift.");
    }
}
