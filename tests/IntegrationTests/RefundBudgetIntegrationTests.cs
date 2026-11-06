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

public sealed class RefundBudgetIntegrationTests
{
    private static string? ConnectionString =>
        Environment.GetEnvironmentVariable("PAYMENT_TEST_CONNECTION_STRING");

    [Fact]
    public async Task ConcurrentRefunds_700kPlus600k_NeverExceedOneMillion()
    {
        if(ConnectionString is null)return;
        await using var db=NpgsqlDataSource.Create(ConnectionString);
        var (payment,merchant)=await PaidPaymentAsync(db,1_000_000);
        var service=new RefundService(new NpgsqlRefundStore(db));
        var a=new RequestRefundCommand(merchant,payment,"refund-a-"+Guid.NewGuid().ToString("N"),700_000);
        var b=new RequestRefundCommand(merchant,payment,"refund-b-"+Guid.NewGuid().ToString("N"),600_000);
        var outcomes=await Task.WhenAll(
            SafeRequestAsync(service,a),SafeRequestAsync(service,b));
        Assert.Equal(1,outcomes.Count(o=>o.Created is not null));
        Assert.Equal(1,outcomes.Count(o=>o.Error is InsufficientRefundBudgetException));
        var (reserved,completed,limit)=await BudgetAsync(db,payment);
        Assert.True(reserved<=limit);
        Assert.Equal(0,completed);
        Assert.Equal(1_000_000,limit);
        Assert.True(reserved is 700_000 or 600_000);

        // A subsequent request may use only the remaining budget.
        var remaining=limit-reserved;
        var next=await service.RequestAsync(new RequestRefundCommand(merchant,payment,
            "remainder-"+Guid.NewGuid().ToString("N"),remaining),CancellationToken.None);
        Assert.NotNull(next.Refund);
        var totals=await BudgetAsync(db,payment);
        Assert.Equal(limit,totals.reserved);
        Assert.Equal(0,totals.completed);
    }

    [Fact]
    public async Task IdempotencyReplay_DoesNotDoubleReserve_ConflictingAmountFails()
    {
        if(ConnectionString is null)return;
        await using var db=NpgsqlDataSource.Create(ConnectionString);
        var (payment,merchant)=await PaidPaymentAsync(db,500_000);
        var service=new RefundService(new NpgsqlRefundStore(db));
        var cmd=new RequestRefundCommand(merchant,payment,
            "same-refund-"+Guid.NewGuid().ToString("N"),200_000);
        var first=await service.RequestAsync(cmd,CancellationToken.None);
        var replay=await service.RequestAsync(cmd,CancellationToken.None);
        Assert.Equal(first.Refund.RefundId,replay.Refund.RefundId);
        Assert.True(replay.Replayed);
        await Assert.ThrowsAsync<RefundConflictException>(()=>
            service.RequestAsync(cmd with{AmountMinor=210_000},CancellationToken.None));
        var budget=await BudgetAsync(db,payment);
        Assert.Equal(200_000,budget.reserved);
    }

    [Fact]
    public async Task PendingOrOtherMerchantPayment_IsNotRefundable()
    {
        if(ConnectionString is null)return;
        await using var db=NpgsqlDataSource.Create(ConnectionString);
        var merchant=Guid.NewGuid();
        var paymentService=new PaymentService(new NpgsqlPaymentStore(db));
        var created=await paymentService.CreateAsync(new CreatePaymentCommand(merchant,
            "pending-"+Guid.NewGuid().ToString("N"),"pending-order",100_000,"VND","MOCK"),CancellationToken.None);
        var refund=new RefundService(new NpgsqlRefundStore(db));
        await Assert.ThrowsAsync<PaymentNotRefundableException>(()=>
            refund.RequestAsync(new RequestRefundCommand(merchant,created.Payment.PaymentId,
                "nonpaid-"+Guid.NewGuid().ToString("N"),50_000),CancellationToken.None));
        await Assert.ThrowsAsync<PaymentNotRefundableException>(()=>
            refund.RequestAsync(new RequestRefundCommand(Guid.NewGuid(),created.Payment.PaymentId,
                "othermerchant-"+Guid.NewGuid().ToString("N"),50_000),CancellationToken.None));
    }

    [Fact]
    public async Task FailedRefund_ReleasesReservationForAnotherRequest()
    {
        if(ConnectionString is null)return;
        await using var db=NpgsqlDataSource.Create(ConnectionString);
        var (payment,merchant)=await PaidPaymentAsync(db,100_000);
        var service=new RefundService(new NpgsqlRefundStore(db));
        var created=await service.RequestAsync(new RequestRefundCommand(merchant,payment,
            "fail-"+Guid.NewGuid().ToString("N"),100_000),CancellationToken.None);
        var process=new NpgsqlRefundProcessing(db);
        var claim=await process.ClaimRefundAsync(created.Refund.RefundId,CancellationToken.None);
        Assert.NotNull(claim);
        var provider=new NpgsqlMockRefundProvider(db);
        Assert.Equal(RefundState.Failed,await provider.InitiateAsync(claim!.ProviderKey,
            MockRefundBehavior.Failed,CancellationToken.None));
        Assert.True(await process.CompleteAsync(claim,RefundState.Failed,CancellationToken.None));
        var budget=await BudgetAsync(db,payment);
        Assert.Equal(0,budget.reserved);
        Assert.Equal(0,budget.completed);
        var second=await service.RequestAsync(new RequestRefundCommand(merchant,payment,
            "retry-different-request-"+Guid.NewGuid().ToString("N"),100_000),CancellationToken.None);
        Assert.Equal(RefundState.Requested,second.Refund.State);
    }

    [Fact]
    public async Task TimeoutAfterPspRefunded_KeepsReservationUntilVerifiedInquiry()
    {
        if(ConnectionString is null)return;
        await using var db=NpgsqlDataSource.Create(ConnectionString);
        var (payment,merchant)=await PaidPaymentAsync(db,400_000);
        var service=new RefundService(new NpgsqlRefundStore(db));
        var created=await service.RequestAsync(new RequestRefundCommand(merchant,payment,
            "unknown-"+Guid.NewGuid().ToString("N"),250_000),CancellationToken.None);
        var processing=new NpgsqlRefundProcessing(db);
        var claim=await processing.ClaimRefundAsync(created.Refund.RefundId,CancellationToken.None);
        Assert.NotNull(claim);
        var provider=new NpgsqlMockRefundProvider(db);
        Assert.Equal(RefundState.Unknown,await provider.InitiateAsync(claim!.ProviderKey,
            MockRefundBehavior.TimeoutAfterSuccess,CancellationToken.None));
        Assert.True(await processing.CompleteAsync(claim,RefundState.Unknown,CancellationToken.None));
        var uncertain=await service.GetAsync(merchant,created.Refund.RefundId,CancellationToken.None);
        Assert.Equal(RefundState.Unknown,uncertain!.State);
        var held=await BudgetAsync(db,payment);
        Assert.Equal(250_000,held.reserved);
        Assert.Equal(0,held.completed);

        var recovery=new NpgsqlRefundRecovery(db,provider);
        Assert.Equal(RefundRecoveryResult.VerifiedSuccess,
            await recovery.RecoverOneAsync(created.Refund.RefundId,CancellationToken.None));
        var settled=await BudgetAsync(db,payment);
        Assert.Equal(0,settled.reserved);
        Assert.Equal(250_000,settled.completed);
        Assert.Equal(RefundRecoveryResult.NoWork,
            await recovery.RecoverOneAsync(created.Refund.RefundId,CancellationToken.None));
    }

    [Fact]
    public async Task UnknownWithoutEvidence_NeverReleasesBudgetOrClaimsFailure()
    {
        if(ConnectionString is null)return;
        await using var db=NpgsqlDataSource.Create(ConnectionString);
        var (payment,merchant)=await PaidPaymentAsync(db,300_000);
        var service=new RefundService(new NpgsqlRefundStore(db));
        var created=await service.RequestAsync(new RequestRefundCommand(merchant,payment,
            "noeffect-"+Guid.NewGuid().ToString("N"),150_000),CancellationToken.None);
        var process=new NpgsqlRefundProcessing(db);
        var claim=await process.ClaimRefundAsync(created.Refund.RefundId,CancellationToken.None);
        Assert.NotNull(claim);
        var provider=new NpgsqlMockRefundProvider(db);
        Assert.Equal(RefundState.Unknown,await provider.InitiateAsync(claim!.ProviderKey,
            MockRefundBehavior.TimeoutBeforeEffect,CancellationToken.None));
        Assert.True(await process.CompleteAsync(claim,RefundState.Unknown,CancellationToken.None));
        Assert.Equal(RefundRecoveryResult.Unresolved,
            await new NpgsqlRefundRecovery(db,provider).RecoverOneAsync(
                created.Refund.RefundId,CancellationToken.None));
        var result=await BudgetAsync(db,payment);
        Assert.Equal(150_000,result.reserved);
        Assert.Equal(0,result.completed);
    }

    [Fact]
    public async Task ConcurrentRefundInquiries_ExactlyOneSettlementAndOutbox()
    {
        if(ConnectionString is null)return;
        await using var db=NpgsqlDataSource.Create(ConnectionString);
        var (payment,merchant)=await PaidPaymentAsync(db,200_000);
        var service=new RefundService(new NpgsqlRefundStore(db));
        var created=await service.RequestAsync(new RequestRefundCommand(merchant,payment,
            "inquiries-"+Guid.NewGuid().ToString("N"),200_000),CancellationToken.None);
        var process=new NpgsqlRefundProcessing(db);
        var claim=await process.ClaimRefundAsync(created.Refund.RefundId,CancellationToken.None);
        Assert.NotNull(claim);
        var provider=new NpgsqlMockRefundProvider(db);
        await provider.InitiateAsync(claim!.ProviderKey,MockRefundBehavior.TimeoutAfterSuccess,CancellationToken.None);
        Assert.True(await process.CompleteAsync(claim,RefundState.Unknown,CancellationToken.None));
        var recovery=new NpgsqlRefundRecovery(db,provider);
        var results=await Task.WhenAll(Enumerable.Range(0,12).Select(_=>
            recovery.RecoverOneAsync(created.Refund.RefundId,CancellationToken.None)));
        Assert.Equal(1,results.Count(x=>x==RefundRecoveryResult.VerifiedSuccess));
        Assert.All(results,x=>Assert.True(x is RefundRecoveryResult.VerifiedSuccess or RefundRecoveryResult.NoWork));

        await using var conn=await db.OpenConnectionAsync();
        await using var cmd=new NpgsqlCommand("""
            SELECT count(*) FROM messaging.outbox_messages
            WHERE aggregate_id=@refund AND event_type='RefundSucceeded'
            """,conn);
        cmd.Parameters.AddWithValue("refund",created.Refund.RefundId);
        Assert.Equal(1L,(long)(await cmd.ExecuteScalarAsync())!);
        var budget=await BudgetAsync(db,payment);
        Assert.Equal(0,budget.reserved);
        Assert.Equal(200_000,budget.completed);
    }

    private static async Task<(RefundCreationResult? Created,Exception? Error)> SafeRequestAsync(
        RefundService svc,RequestRefundCommand cmd)
    {
        try {return(await svc.RequestAsync(cmd,CancellationToken.None),null);}
        catch(Exception e){return(null,e);}
    }

    private static async Task<(long reserved,long completed,long limit)> BudgetAsync(
        NpgsqlDataSource db,Guid payment)
    {
        await using var conn=await db.OpenConnectionAsync();
        await using var cmd=new NpgsqlCommand("""
            SELECT reserved_minor,completed_minor,verified_limit_minor
            FROM refund.refund_budgets WHERE payment_id=@payment
            """,conn);
        cmd.Parameters.AddWithValue("payment",payment);
        await using var rd=await cmd.ExecuteReaderAsync();
        Assert.True(await rd.ReadAsync());
        return(rd.GetInt64(0),rd.GetInt64(1),rd.GetInt64(2));
    }

    private static async Task<(Guid paymentId,Guid merchant)> PaidPaymentAsync(NpgsqlDataSource db,long amount)
    {
        var merchant=Guid.NewGuid();
        var pay=new PaymentService(new NpgsqlPaymentStore(db));
        var created=await pay.CreateAsync(new CreatePaymentCommand(merchant,
            "paid-"+Guid.NewGuid().ToString("N"),"refund-demo-payment",
            amount,"VND","MOCK"),CancellationToken.None);
        await using var conn=await db.OpenConnectionAsync();
        Guid attempt;
        string providerKey;
        await using (var select=new NpgsqlCommand("""
            SELECT attempt_id,provider_operation_key FROM payment.payment_attempts
            WHERE payment_id=@payment
            """,conn))
        {
            select.Parameters.AddWithValue("payment",created.Payment.PaymentId);
            await using var rd=await select.ExecuteReaderAsync();
            Assert.True(await rd.ReadAsync());
            attempt=rd.GetGuid(0);
            providerKey=rd.GetString(1);
        }
        var payload=new MockSignedWebhook(Guid.NewGuid(),created.Payment.PaymentId,
            attempt,providerKey,amount,"VND","Succeeded");
        Assert.Equal(MockWebhookDisposition.Applied,
            await new NpgsqlMockWebhookProcessor(db).ApplyVerifiedAsync(
                payload,Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)),CancellationToken.None));
        return(created.Payment.PaymentId,merchant);
    }
}
