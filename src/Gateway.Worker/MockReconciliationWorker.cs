using PaymentGateway.Payments.Infrastructure;

namespace PaymentGateway.Worker;

/// <summary>Simulated independent reconciliation report; no direct writes to Payment.</summary>
public sealed class MockReconciliationWorker(
    NpgsqlMockReconciliationStore reconciliation,
    ILogger<MockReconciliationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        do
        {
            try
            {
                var mismatch = await reconciliation.ReconcileOneAsync(ct);
                if (mismatch is not null)
                    logger.LogWarning("Mock PSP mismatch for payment {PaymentId}: provider={Provider}, gateway={Gateway}",
                        mismatch.PaymentId, mismatch.ProviderOutcome, mismatch.GatewayOutcome);
                await reconciliation.CloseResolvedAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Mock reconciliation scan failed.");
            }
        } while (await timer.WaitForNextTickAsync(ct));
    }
}
