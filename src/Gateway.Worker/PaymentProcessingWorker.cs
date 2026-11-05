using PaymentGateway.Payments.Domain;
using PaymentGateway.Payments.Infrastructure;

namespace PaymentGateway.Worker;

public sealed class MockPsp(IConfiguration config)
{
    // All outcomes are synthetic. This class performs NO bank/PSP network requests.
    public async Task<PaymentState> InitiateAsync(ClaimedAttempt attempt, CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(50), ct);
        return config["MockPsp:Outcome"] switch
        {
            "Succeeded" => PaymentState.Succeeded, // SIMULATED confirmed paid
            "Failed" => PaymentState.Failed,       // SIMULATED definitive no-effect decline
            "Timeout" => PaymentState.Unknown,     // SIMULATED ambiguous result
            _ => throw new InvalidOperationException("MockPsp:Outcome must be Succeeded, Failed or Timeout.")
        };
    }
}

public sealed class PaymentProcessingWorker(
    NpgsqlPaymentWorkStore store, MockPsp psp, ILogger<PaymentProcessingWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var tick = new PeriodicTimer(TimeSpan.FromSeconds(1));
        do
        {
            try
            {
                // An expired claim might already have caused an external PSP side effect;
                // move to UNKNOWN, NOT back to the Pending queue.
                if (await store.MarkOneExpiredClaimUnknownAsync(stoppingToken))
                    continue;

                var attempt = await store.ClaimPendingAsync(stoppingToken);
                if (attempt is null)
                    continue;

                var simulatedOutcome = await psp.InitiateAsync(attempt, stoppingToken);
                var committed = await store.CompleteAsync(attempt, simulatedOutcome, stoppingToken);
                if (!committed)
                    logger.LogWarning("Attempt {AttemptId} claim was superseded; no repeat call.", attempt.AttemptId);
                else
                    logger.LogInformation("Demo payment {PaymentId}: {Outcome}", attempt.PaymentId, simulatedOutcome);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Do not reissue the ambiguous PSP attempt here.
                logger.LogError(ex, "Demo worker iteration failed; processing attempts remain claimed until UNKNOWN recovery.");
            }
        }
        while (await tick.WaitForNextTickAsync(stoppingToken));
    }
}
