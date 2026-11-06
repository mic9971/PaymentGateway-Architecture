using Npgsql;

namespace PaymentGateway.Refunds.Infrastructure;

/// <summary>
/// Only two *distinct, registered workforce principals* may release a high-value
/// Refund. The requester can never approve their own request. This model is
/// development-only: production needs a real workforce IdP and approval policies.
/// </summary>
public enum RefundApprovalOutcome
{
    NotFound, NotPending, Unauthorized, SelfApprovalForbidden,
    AlreadyApproved, FirstApprovalRecorded, ReleasedForProcessing
}

public sealed class NpgsqlRefundApprovalStore(NpgsqlDataSource source)
{
    public async Task<RefundApprovalOutcome> ApproveAsync(
        Guid merchantId, Guid refundId, string issuer, string approverSubject,
        string approverClientId, CancellationToken ct)
    {
        if (merchantId == Guid.Empty || refundId == Guid.Empty ||
            string.IsNullOrWhiteSpace(issuer) ||
            string.IsNullOrWhiteSpace(approverSubject) ||
            string.IsNullOrWhiteSpace(approverClientId) ||
            approverSubject.Length > 256 || approverClientId.Length > 256)
            return RefundApprovalOutcome.Unauthorized;

        await using var conn=await source.OpenConnectionAsync(ct);
        await using var tx=await conn.BeginTransactionAsync(ct);

        Guid paymentId;
        long amount;
        string state, initiator;
        bool required;
        // SERIALIZATION POINT: every competing approver locks the SAME refund row.
        await using (var row=new NpgsqlCommand("""
            SELECT payment_id,amount_minor,state,approval_required,request_actor_subject
            FROM refund.refund_requests
            WHERE refund_id=@refund AND merchant_id=@merchant
            FOR UPDATE
            """,conn,tx))
        {
            row.Parameters.AddWithValue("refund",refundId);
            row.Parameters.AddWithValue("merchant",merchantId);
            await using var reader=await row.ExecuteReaderAsync(ct);
            if(!await reader.ReadAsync(ct))return RefundApprovalOutcome.NotFound;
            paymentId=reader.GetGuid(0);
            amount=reader.GetInt64(1);
            state=reader.GetString(2);
            required=reader.GetBoolean(3);
            initiator=reader.IsDBNull(4)?"":reader.GetString(4);
        }

        if(!required || state!="PendingApproval")
            return RefundApprovalOutcome.NotPending;
        if (string.Equals(initiator,approverSubject,StringComparison.Ordinal))
            return RefundApprovalOutcome.SelfApprovalForbidden;

        // The check is repeated INSIDE the financial transaction rather than
        // trusting HTTP policy alone; a committed suspension/grant revocation
        // before this check must prevent approval. FOR SHARE locks rows until commit.
        await using (var allowed=new NpgsqlCommand("""
            SELECT 1 FROM iam.merchants m
            JOIN iam.merchant_clients c ON c.merchant_id=m.merchant_id
            WHERE m.merchant_id=@merchant AND m.status='Active'
              AND c.enabled AND c.issuer=@issuer AND c.subject=@subject
              AND c.client_id=@client AND 'refunds:approve'=ANY(c.granted_scopes)
            LIMIT 1 FOR SHARE OF m,c
            """,conn,tx))
        {
            allowed.Parameters.AddWithValue("merchant",merchantId);
            allowed.Parameters.AddWithValue("issuer",issuer);
            allowed.Parameters.AddWithValue("subject",approverSubject);
            allowed.Parameters.AddWithValue("client",approverClientId);
            if(await allowed.ExecuteScalarAsync(ct) is null)
                return RefundApprovalOutcome.Unauthorized;
        }

        await using (var approve=new NpgsqlCommand("""
            INSERT INTO refund.refund_approvals
              (refund_id,merchant_id,actor_subject,actor_client,issuer,decision)
            VALUES (@refund,@merchant,@subject,@client,@issuer,'Approved')
            ON CONFLICT (refund_id,actor_subject) DO NOTHING
            RETURNING actor_subject
            """,conn,tx))
        {
            approve.Parameters.AddWithValue("refund",refundId);
            approve.Parameters.AddWithValue("merchant",merchantId);
            approve.Parameters.AddWithValue("subject",approverSubject);
            approve.Parameters.AddWithValue("client",approverClientId);
            approve.Parameters.AddWithValue("issuer",issuer);
            if(await approve.ExecuteScalarAsync(ct) is null)
            {
                await tx.CommitAsync(ct);
                return RefundApprovalOutcome.AlreadyApproved;
            }
        }

        await RefundFinancialAudit.AppendAsync(conn,tx,refundId,paymentId,merchantId,
            approverSubject,approverClientId,"RefundApprovalRecorded",amount,ct);

        int count;
        await using (var total=new NpgsqlCommand("""
            SELECT count(*) FROM refund.refund_approvals WHERE refund_id=@refund
            """,conn,tx))
        {
            total.Parameters.AddWithValue("refund",refundId);
            count=Convert.ToInt32(await total.ExecuteScalarAsync(ct));
        }
        if(count>2)
            throw new InvalidOperationException("Inconsistent refund approval count.");

        if(count==2)
        {
            await using(var release=new NpgsqlCommand("""
                UPDATE refund.refund_requests
                SET state='Requested',updated_at=now()
                WHERE refund_id=@refund AND state='PendingApproval' AND approval_required
                """,conn,tx))
            {
                release.Parameters.AddWithValue("refund",refundId);
                if(await release.ExecuteNonQueryAsync(ct)!=1)
                    throw new InvalidOperationException("Refund approval transition lost.");
            }

            await RefundFinancialAudit.AppendAsync(conn,tx,refundId,paymentId,merchantId,
                approverSubject,approverClientId,"RefundReleased",amount,ct);
            await RefundOutbox.AppendAsync(conn,tx,"RefundRequested",refundId,
                new{refund_id=refundId,payment_id=paymentId,
                    approved_by_two_distinct_actors=true,simulated=true},ct);
        }
        else
        {
            await RefundOutbox.AppendAsync(conn,tx,"RefundApprovalRecorded",refundId,
                new{refund_id=refundId,payment_id=paymentId,
                    approvals_count=1,simulated=true},ct);
        }
        await tx.CommitAsync(ct);
        return count==2
            ? RefundApprovalOutcome.ReleasedForProcessing
            : RefundApprovalOutcome.FirstApprovalRecorded;
    }
}
