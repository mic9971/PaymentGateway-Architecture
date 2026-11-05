using PaymentGateway.Payments.Infrastructure;

namespace PaymentGateway.Worker;

/// <summary>Read-only mock PSP inquiries. No payment initiation or retry.</summary>
public sealed class PaymentRecoveryWorker(
    PaymentRecoveryService recovery, ILogger<PaymentRecoveryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        do
        {
            try
            {
                var result = await recovery.RecoverOneAsync(ct);
                if (result is RecoveryResult.VerifiedSuccess or RecoveryResult.VerifiedFailure)
                    logger.LogInformation("Simulated PSP inquiry reconciled payment: {Outcome}", result);
                else if (result is RecoveryResult.Unresolved)
                    logger.LogWarning("Mock PSP inquiry inconclusive; payment remains Unknown");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Read-only recovery failed; payment remains safe/Unknown");
            }
        } while (await timer.WaitForNextTickAsync(ct));
    }
}
