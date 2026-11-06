using PaymentGateway.Refunds.Domain;
using PaymentGateway.Refunds.Infrastructure;

namespace PaymentGateway.Worker;

/// <summary>Development-only, no PSP network; UNKNOWN refund keeps its reserved funds.</summary>
public sealed class RefundWorker(
    NpgsqlRefundProcessing processing,NpgsqlMockRefundProvider provider,
    NpgsqlRefundRecovery recovery,IConfiguration configuration,
    ILogger<RefundWorker> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer=new PeriodicTimer(TimeSpan.FromSeconds(1));
        do
        {
            try
            {
                if(await processing.ExpireOneProcessingAsync(ct))continue;
                var claim=await processing.ClaimOneAsync(ct);
                if(claim is not null)
                {
                    var raw=configuration["MockRefund:Outcome"]??"Succeeded";
                    if(!Enum.TryParse<MockRefundBehavior>(raw,true,out var behavior))
                        throw new InvalidOperationException("Unknown MockRefund:Outcome.");
                    var result=await provider.InitiateAsync(claim.ProviderKey,behavior,ct);
                    var applied=await processing.CompleteAsync(claim,result,ct);
                    if(!applied)
                        logger.LogWarning("Refund {RefundId} had a stale claim; no reissue",claim.RefundId);
                }
                var recoveryResult=await recovery.RecoverOneAsync(null,ct);
                if(recoveryResult is RefundRecoveryResult.VerifiedSuccess or RefundRecoveryResult.VerifiedFailure)
                    logger.LogInformation("Mock refund inquiry resolved result {Result}",recoveryResult);
            }
            catch(OperationCanceledException)when(ct.IsCancellationRequested){break;}
            catch(Exception ex){logger.LogError(ex,"Mock refund processing failed; no automatic reissue.");}
        }while(await timer.WaitForNextTickAsync(ct));
    }
}
