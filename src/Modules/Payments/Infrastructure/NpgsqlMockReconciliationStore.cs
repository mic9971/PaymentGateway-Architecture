using Npgsql;

namespace PaymentGateway.Payments.Infrastructure;

public sealed record MockDiscrepancy(
    Guid PaymentId, string ProviderOperationKey, string ProviderOutcome, string GatewayOutcome);

/// <summary>
/// Read-only evidence comparison of simulated PSP ledger vs Gateway Payment state.
/// Owns only reconciliation.* data. Does not overwrite the Payment bounded context.
/// </summary>
public sealed class NpgsqlMockReconciliationStore(NpgsqlDataSource source)
{
    public async Task<MockDiscrepancy?> ReconcileOneAsync(CancellationToken ct)
    {
        await using var conn = await source.OpenConnectionAsync(ct);
        // Store one discrepancy per scan to keep each transaction small.
        await using var pick = new NpgsqlCommand("""
            SELECT a.payment_id, o.provider_operation_key, o.outcome, p.state
            FROM mockpsp.provider_operations o
            JOIN payment.payment_attempts a
                ON a.provider_operation_key=o.provider_operation_key
            JOIN payment.payment_intents p ON p.payment_id=a.payment_id
            WHERE p.state <> o.outcome
            ORDER BY o.recorded_at, o.provider_operation_key LIMIT 1
            """, conn);
        Guid paymentId;
        string key, providerOutcome, gatewayOutcome;
        await using (var rd = await pick.ExecuteReaderAsync(ct))
        {
            if (!await rd.ReadAsync(ct)) return null;
            paymentId = rd.GetGuid(0);
            key = rd.GetString(1);
            providerOutcome = rd.GetString(2);
            gatewayOutcome = rd.GetString(3);
        }

        await using var record = new NpgsqlCommand("""
            INSERT INTO reconciliation.mock_discrepancies
              (provider_operation_key,payment_id,provider_outcome,gateway_outcome,reason)
            VALUES (@key,@payment,@provider,@gateway,'GatewayProviderMismatch')
            ON CONFLICT (provider_operation_key) DO UPDATE
            SET provider_outcome=EXCLUDED.provider_outcome,
                gateway_outcome=EXCLUDED.gateway_outcome,
                observed_at=now(),resolved_at=NULL
            """, conn);
        record.Parameters.AddWithValue("key", key);
        record.Parameters.AddWithValue("payment", paymentId);
        record.Parameters.AddWithValue("provider", providerOutcome);
        record.Parameters.AddWithValue("gateway", gatewayOutcome);
        await record.ExecuteNonQueryAsync(ct);
        return new MockDiscrepancy(paymentId, key, providerOutcome, gatewayOutcome);
    }

    public async Task<int> CloseResolvedAsync(CancellationToken ct)
    {
        await using var conn = await source.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            UPDATE reconciliation.mock_discrepancies d
            SET resolved_at=now()
            FROM payment.payment_attempts a
            JOIN payment.payment_intents p ON p.payment_id=a.payment_id
            WHERE d.provider_operation_key=a.provider_operation_key
              AND d.resolved_at IS NULL
              AND p.state=d.provider_outcome
            """, conn);
        return await cmd.ExecuteNonQueryAsync(ct);
    }
}
