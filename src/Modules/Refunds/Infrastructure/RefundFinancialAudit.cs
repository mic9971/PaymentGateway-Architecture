using Npgsql;

namespace PaymentGateway.Refunds.Infrastructure;

/// <summary>
/// A durable append-only-for-application code event record, committed in the
/// SAME PostgreSQL transaction as Refund state and Outbox. Does not claim WORM
/// storage, independent signatures or regulatory-grade non-repudiation.
/// </summary>
internal static class RefundFinancialAudit
{
    public static async Task AppendAsync(
        NpgsqlConnection conn,NpgsqlTransaction tx,Guid refundId,Guid paymentId,
        Guid merchantId,string? actorSubject,string? actorClient,
        string action,long amount,CancellationToken ct)
    {
        await using var cmd=new NpgsqlCommand("""
            INSERT INTO finance_audit.refund_actions
              (event_id,refund_id,payment_id,merchant_id,
               actor_subject,actor_client,action_kind,amount_minor)
            VALUES (@event,@refund,@payment,@merchant,
                    @subject,@client,@action,@amount)
            """,conn,tx);
        cmd.Parameters.AddWithValue("event",Guid.NewGuid());
        cmd.Parameters.AddWithValue("refund",refundId);
        cmd.Parameters.AddWithValue("payment",paymentId);
        cmd.Parameters.AddWithValue("merchant",merchantId);
        cmd.Parameters.AddWithValue("subject",(object?)actorSubject??DBNull.Value);
        cmd.Parameters.AddWithValue("client",(object?)actorClient??DBNull.Value);
        cmd.Parameters.AddWithValue("action",action);
        cmd.Parameters.AddWithValue("amount",amount);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
