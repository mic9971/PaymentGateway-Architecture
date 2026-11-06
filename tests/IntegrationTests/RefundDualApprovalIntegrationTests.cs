using System.Text;
using System.Text.Json;
using Npgsql;
using PaymentGateway.Payments.Application;
using PaymentGateway.Payments.Infrastructure;
using PaymentGateway.Refunds.Application;
using PaymentGateway.Refunds.Domain;
using PaymentGateway.Refunds.Infrastructure;
using Xunit;

namespace PaymentGateway.Tests.Integration;

public sealed class RefundDualApprovalIntegrationTests
{
    private static string? Pg => Environment.GetEnvironmentVariable("PAYMENT_TEST_CONNECTION_STRING");
    private const string Issuer = "https://merchant-auth.local.test";
    private const string Initiator = "merchant-requester";
    private const string InitiatorClient = "merchant-client";

    [Fact]
    public async Task HighValueRefund_IsNeverClaimedUntilTwoDistinctApproversAuthorize()
    {
        if(Pg is null)return;
        await using var db=NpgsqlDataSource.Create(Pg);
        var (merchant,payment,refund)=await NewHighRefundAsync(db);
        var process=new NpgsqlRefundProcessing(db);
        Assert.Equal(RefundState.PendingApproval,refund.Refund.State);
        Assert.Null(await process.ClaimRefundAsync(refund.Refund.RefundId,CancellationToken.None));

        var approval=new NpgsqlRefundApprovalStore(db);
        await RegisterAsync(db,merchant,"finance-person-a","finance-client-a");
        await RegisterAsync(db,merchant,"finance-person-b","finance-client-b");
        var first=await approval.ApproveAsync(merchant,refund.Refund.RefundId,Issuer,
            "finance-person-a","finance-client-a",CancellationToken.None);
        Assert.Equal(RefundApprovalOutcome.FirstApprovalRecorded,first);
        Assert.Null(await process.ClaimRefundAsync(refund.Refund.RefundId,CancellationToken.None));
        Assert.Equal(RefundApprovalOutcome.AlreadyApproved,
            await approval.ApproveAsync(merchant,refund.Refund.RefundId,Issuer,
                "finance-person-a","finance-client-a",CancellationToken.None));

        var second=await approval.ApproveAsync(merchant,refund.Refund.RefundId,Issuer,
            "finance-person-b","finance-client-b",CancellationToken.None);
        Assert.Equal(RefundApprovalOutcome.ReleasedForProcessing,second);

        var service=new RefundService(new NpgsqlRefundStore(db));
        var released=await service.GetAsync(merchant,refund.Refund.RefundId,CancellationToken.None);
        Assert.Equal(RefundState.Requested,released!.State);
        var claimed=await process.ClaimRefundAsync(refund.Refund.RefundId,CancellationToken.None);
        Assert.NotNull(claimed);
        Assert.Equal(RefundApprovalOutcome.NotPending,
            await approval.ApproveAsync(merchant,refund.Refund.RefundId,Issuer,
                "finance-person-a","finance-client-a",CancellationToken.None));

        await using var conn=await db.OpenConnectionAsync();
        Assert.Equal(2L,await CountAsync(conn,
            "SELECT count(*) FROM refund.refund_approvals WHERE refund_id=@id",refund.Refund.RefundId));
        Assert.Equal(1L,await CountAsync(conn,
            "SELECT count(*) FROM messaging.outbox_messages WHERE aggregate_id=@id AND event_type='RefundRequested'",
            refund.Refund.RefundId));
        Assert.Equal(4L,await CountAsync(conn,
            "SELECT count(*) FROM finance_audit.refund_actions WHERE refund_id=@id",refund.Refund.RefundId));
        // The original financial budget remains reserved until a real result is known.
        await using var budget=new NpgsqlCommand("""
            SELECT reserved_minor,completed_minor FROM refund.refund_budgets
            WHERE payment_id=@id
            """,conn);
        budget.Parameters.AddWithValue("id",payment);
        await using var result=await budget.ExecuteReaderAsync();
        Assert.True(await result.ReadAsync());
        Assert.Equal(1_200_000,result.GetInt64(0));
        Assert.Equal(0,result.GetInt64(1));
    }

    [Fact]
    public async Task RequesterCannotApproveOwnRefund_OrBypassIssuerAndTenant()
    {
        if(Pg is null)return;
        await using var db=NpgsqlDataSource.Create(Pg);
        var (merchant,_,refund)=await NewHighRefundAsync(db);
        await RegisterAsync(db,merchant,Initiator,InitiatorClient);
        var approval=new NpgsqlRefundApprovalStore(db);
        Assert.Equal(RefundApprovalOutcome.SelfApprovalForbidden,
            await approval.ApproveAsync(merchant,refund.Refund.RefundId,Issuer,
                Initiator,InitiatorClient,CancellationToken.None));
        Assert.Equal(RefundApprovalOutcome.NotFound,
            await approval.ApproveAsync(Guid.NewGuid(),refund.Refund.RefundId,Issuer,
                Initiator,InitiatorClient,CancellationToken.None));
        Assert.Equal(RefundApprovalOutcome.Unauthorized,
            await approval.ApproveAsync(merchant,refund.Refund.RefundId,"https://wrong-issuer",
                "finance-person-a","finance-client-a",CancellationToken.None));
        await using var conn=await db.OpenConnectionAsync();
        Assert.Equal(0L,await CountAsync(conn,
            "SELECT count(*) FROM refund.refund_approvals WHERE refund_id=@id",refund.Refund.RefundId));
    }

    [Fact]
    public async Task SuspendedApproverOrRevokedDbGrant_IsRejectedInApprovalTransaction()
    {
        if(Pg is null)return;
        await using var db=NpgsqlDataSource.Create(Pg);
        var (merchant,_,refund)=await NewHighRefundAsync(db);
        await RegisterAsync(db,merchant,"finance-a","client-a");
        await RegisterAsync(db,merchant,"finance-b","client-b");
        var store=new NpgsqlRefundApprovalStore(db);
        Assert.Equal(RefundApprovalOutcome.FirstApprovalRecorded,
            await store.ApproveAsync(merchant,refund.Refund.RefundId,Issuer,
                "finance-a","client-a",CancellationToken.None));
        await using(var conn=await db.OpenConnectionAsync())
        await using(var revoke=new NpgsqlCommand("""
            UPDATE iam.merchant_clients SET granted_scopes=ARRAY['refunds:read']::text[],
                updated_at=now()
            WHERE merchant_id=@merchant AND subject='finance-b'
            """,conn))
        {
            revoke.Parameters.AddWithValue("merchant",merchant);
            Assert.Equal(1,await revoke.ExecuteNonQueryAsync());
        }
        Assert.Equal(RefundApprovalOutcome.Unauthorized,
            await store.ApproveAsync(merchant,refund.Refund.RefundId,Issuer,
                "finance-b","client-b",CancellationToken.None));
        var get=await new RefundService(new NpgsqlRefundStore(db))
            .GetAsync(merchant,refund.Refund.RefundId,CancellationToken.None);
        Assert.Equal(RefundState.PendingApproval,get!.State);
        await using(var conn=await db.OpenConnectionAsync())
        await using(var grant=new NpgsqlCommand("""
            UPDATE iam.merchant_clients SET granted_scopes=ARRAY['refunds:approve']::text[],
                updated_at=now()
            WHERE merchant_id=@merchant AND subject='finance-b'
            """,conn))
        {
            grant.Parameters.AddWithValue("merchant",merchant);
            await grant.ExecuteNonQueryAsync();
        }
        Assert.Equal(RefundApprovalOutcome.ReleasedForProcessing,
            await store.ApproveAsync(merchant,refund.Refund.RefundId,Issuer,
                "finance-b","client-b",CancellationToken.None));
    }

    [Fact]
    public async Task TwelveConcurrentApprovalCalls_ReleaseRefundOnlyOnce()
    {
        if(Pg is null)return;
        await using var db=NpgsqlDataSource.Create(Pg);
        var (merchant,_,refund)=await NewHighRefundAsync(db);
        await RegisterAsync(db,merchant,"parallel-a","finance-a");
        await RegisterAsync(db,merchant,"parallel-b","finance-b");
        var store=new NpgsqlRefundApprovalStore(db);
        var results=await Task.WhenAll(Enumerable.Range(0,12).Select(i=>
            store.ApproveAsync(merchant,refund.Refund.RefundId,Issuer,
                i%2==0?"parallel-a":"parallel-b",
                i%2==0?"finance-a":"finance-b",CancellationToken.None)));
        Assert.Equal(1,results.Count(x=>x==RefundApprovalOutcome.FirstApprovalRecorded));
        Assert.Equal(1,results.Count(x=>x==RefundApprovalOutcome.ReleasedForProcessing));
        Assert.All(results,x=>Assert.True(x is RefundApprovalOutcome.FirstApprovalRecorded or
            RefundApprovalOutcome.ReleasedForProcessing or RefundApprovalOutcome.AlreadyApproved
            or RefundApprovalOutcome.NotPending));

        await using var conn=await db.OpenConnectionAsync();
        Assert.Equal(2L,await CountAsync(conn,
            "SELECT count(*) FROM refund.refund_approvals WHERE refund_id=@id",refund.Refund.RefundId));
        Assert.Equal(1L,await CountAsync(conn,
            "SELECT count(*) FROM messaging.outbox_messages WHERE aggregate_id=@id AND event_type='RefundRequested'",
            refund.Refund.RefundId));
    }

    [Fact]
    public async Task FinanceAudit_RejectsUpdateOrDeleteByNormalSqlCommand()
    {
        if(Pg is null)return;
        await using var db=NpgsqlDataSource.Create(Pg);
        var (_,_,refund)=await NewHighRefundAsync(db);
        await using var conn=await db.OpenConnectionAsync();
        await using(var mutate=new NpgsqlCommand("""
            UPDATE finance_audit.refund_actions SET action_kind='RefundReleased'
            WHERE refund_id=@id
            """,conn))
        {
            mutate.Parameters.AddWithValue("id",refund.Refund.RefundId);
            await Assert.ThrowsAsync<PostgresException>(()=>mutate.ExecuteNonQueryAsync());
        }
        await using(var erase=new NpgsqlCommand("""
            DELETE FROM finance_audit.refund_actions WHERE refund_id=@id
            """,conn))
        {
            erase.Parameters.AddWithValue("id",refund.Refund.RefundId);
            await Assert.ThrowsAsync<PostgresException>(()=>erase.ExecuteNonQueryAsync());
        }
        Assert.Equal(1L,await CountAsync(conn,
            "SELECT count(*) FROM finance_audit.refund_actions WHERE refund_id=@id",refund.Refund.RefundId));
    }

    private static async Task<long> CountAsync(NpgsqlConnection conn,string sql,Guid id)
    {
        await using var cmd=new NpgsqlCommand(sql,conn);
        cmd.Parameters.AddWithValue("id",id);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task RegisterAsync(NpgsqlDataSource db,Guid merchant,
        string subject,string clientId)
    {
        await using var conn=await db.OpenConnectionAsync();
        await using var tx=await conn.BeginTransactionAsync();
        await using(var merchantRow=new NpgsqlCommand("""
            INSERT INTO iam.merchants(merchant_id,status) VALUES (@merchant,'Active')
            ON CONFLICT (merchant_id) DO UPDATE SET status='Active'
            """,conn,tx))
        {
            merchantRow.Parameters.AddWithValue("merchant",merchant);
            await merchantRow.ExecuteNonQueryAsync();
        }
        await using(var actor=new NpgsqlCommand("""
            INSERT INTO iam.merchant_clients
              (merchant_id,issuer,subject,client_id,granted_scopes,enabled)
            VALUES (@merchant,@issuer,@subject,@client,ARRAY['refunds:approve']::text[],true)
            """,conn,tx))
        {
            actor.Parameters.AddWithValue("merchant",merchant);
            actor.Parameters.AddWithValue("issuer",Issuer);
            actor.Parameters.AddWithValue("subject",subject);
            actor.Parameters.AddWithValue("client",clientId);
            await actor.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
    }

    private static async Task<(Guid merchant,Guid payment,RefundCreationResult refund)>
        NewHighRefundAsync(NpgsqlDataSource db)
    {
        var merchant=Guid.NewGuid();
        var pay=new PaymentService(new NpgsqlPaymentStore(db));
        var created=await pay.CreateAsync(new CreatePaymentCommand(merchant,
            "approval-payment-"+Guid.NewGuid().ToString("N"),"approval-demo",
            2_000_000,"VND","MOCK"),CancellationToken.None);
        Guid attempt;
        string providerKey;
        await using(var conn=await db.OpenConnectionAsync())
        await using(var command=new NpgsqlCommand("""
            SELECT attempt_id,provider_operation_key FROM payment.payment_attempts
            WHERE payment_id=@payment
            """,conn))
        {
            command.Parameters.AddWithValue("payment",created.Payment.PaymentId);
            await using var reader=await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            attempt=reader.GetGuid(0);
            providerKey=reader.GetString(1);
        }
        var payload=new MockSignedWebhook(Guid.NewGuid(),created.Payment.PaymentId,
            attempt,providerKey,2_000_000,"VND","Succeeded");
        Assert.Equal(MockWebhookDisposition.Applied,
            await new NpgsqlMockWebhookProcessor(db).ApplyVerifiedAsync(
                payload,Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)),CancellationToken.None));
        var refund=await new RefundService(new NpgsqlRefundStore(db)).RequestAsync(
            new RequestRefundCommand(merchant,created.Payment.PaymentId,
                "dual-approval-"+Guid.NewGuid().ToString("N"),1_200_000,
                Initiator,InitiatorClient),CancellationToken.None);
        return (merchant,created.Payment.PaymentId,refund);
    }
}
