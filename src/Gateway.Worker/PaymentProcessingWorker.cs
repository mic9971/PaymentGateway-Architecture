using PaymentGateway.Payments.Domain;
using PaymentGateway.Payments.Infrastructure;

namespace PaymentGateway.Worker;

/// <summary>Only this development worker initiates Mock PSP operations.</summary>
public sealed class PaymentProcessingWorker(
    NpgsqlPaymentWorkStore store, NpgsqlMockPspLedger provider,
    IConfiguration configuration, ILogger<PaymentProcessingWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        do
        {
            try
            {
                if (await store.MarkOneExpiredClaimUnknownAsync(ct)) continue;
                var claim = await store.ClaimPendingAsync(ct);
                if (claim is null) continue;

                var configured = configuration["MockPsp:Outcome"] ?? "Succeeded";
                if (!Enum.TryParse<MockPspBehavior>(configured, ignoreCase: true, out var behavior))
                    throw new InvalidOperationException("Unknown local mock PSP mode: " + configured);

                // Provider may COMMIT its simulated effect, then return Unknown.
                // This is a separate short PostgreSQL transaction from gateway state.
                var outcome = await provider.InitiateAsync(claim.ProviderOperationKey, behavior, ct);
                var committed = await store.CompleteAsync(claim, outcome, ct);
                if (committed)
                    logger.LogInformation("Mock PSP attempt {AttemptId}, payment {PaymentId}: {Outcome}",
                        claim.AttemptId, claim.PaymentId, outcome);
                else
                    logger.LogWarning("Stale attempt claim {AttemptId}; will not repeat PSP initiation", claim.AttemptId);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Mock attempt processing failed; uncertain claims are not reissued.");
            }
        } while (await timer.WaitForNextTickAsync(ct));
    }
}
