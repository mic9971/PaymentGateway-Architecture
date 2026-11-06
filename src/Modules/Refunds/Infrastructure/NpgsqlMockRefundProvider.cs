using Npgsql;
using PaymentGateway.Refunds.Domain;

namespace PaymentGateway.Refunds.Infrastructure;

/// <summary>Simulated PSP refund ledger. No network or real transfers.</summary>
public enum MockRefundBehavior { Succeeded, Failed, TimeoutAfterSuccess, TimeoutBeforeEffect }

public sealed class NpgsqlMockRefundProvider(NpgsqlDataSource source)
{
    public async Task<RefundState> InitiateAsync(string key,MockRefundBehavior behavior,CancellationToken ct)
    {
        if(behavior==MockRefundBehavior.TimeoutBeforeEffect) return RefundState.Unknown;
        var intended=behavior==MockRefundBehavior.Failed?RefundState.Failed:RefundState.Succeeded;
        await using var conn=await source.OpenConnectionAsync(ct);
        await using(var cmd=new NpgsqlCommand("""
            INSERT INTO refund.mock_provider_refunds(provider_operation_key,outcome)
            VALUES (@key,@outcome) ON CONFLICT DO NOTHING
            """,conn))
        {
            cmd.Parameters.AddWithValue("key",key);
            cmd.Parameters.AddWithValue("outcome",intended.ToString());
            await cmd.ExecuteNonQueryAsync(ct);
        }
        var persisted=await QueryAsync(key,ct) ?? throw new InvalidOperationException("Simulated provider lost result.");
        return behavior==MockRefundBehavior.TimeoutAfterSuccess?RefundState.Unknown:persisted;
    }

    /// <summary>Read-only status query, NEVER initiates a refund.</summary>
    public async Task<RefundState?> QueryAsync(string key,CancellationToken ct)
    {
        await using var conn=await source.OpenConnectionAsync(ct);
        await using var cmd=new NpgsqlCommand("""
            SELECT outcome FROM refund.mock_provider_refunds WHERE provider_operation_key=@key
            """,conn);
        cmd.Parameters.AddWithValue("key",key);
        var v=await cmd.ExecuteScalarAsync(ct);
        return v is string state?Enum.Parse<RefundState>(state):null;
    }
}
