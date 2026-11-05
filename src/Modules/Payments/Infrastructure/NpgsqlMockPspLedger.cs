using Npgsql;
using PaymentGateway.Payments.Domain;

namespace PaymentGateway.Payments.Infrastructure;

/// <summary>
/// Local-only fake provider. A separate DB transaction commits the provider's outcome
/// BEFORE the gateway sees an answer. Never treats a timeout as a definitive failure.
/// This is not a real external PSP and does not prove distributed exactly-once behavior.
/// </summary>
public enum MockPspBehavior { Succeeded, Failed, TimeoutAfterSuccess, TimeoutBeforeEffect, Timeout }

public sealed class NpgsqlMockPspLedger(NpgsqlDataSource source)
{
    public async Task<PaymentState> InitiateAsync(
        string providerOperationKey, MockPspBehavior behavior, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(providerOperationKey))
            throw new ArgumentException("Provider operation key is required.");

        // Timeout before any effect is deliberately indistinguishable at the gateway;
        // only a later authoritative inquiry can prove the provider state.
        if (behavior is MockPspBehavior.TimeoutBeforeEffect or MockPspBehavior.Timeout)
            return PaymentState.Unknown;

        var effect = behavior is MockPspBehavior.Failed ? PaymentState.Failed : PaymentState.Succeeded;
        await using var conn = await source.OpenConnectionAsync(ct);
        await using (var cmd = new NpgsqlCommand("""
            INSERT INTO mockpsp.provider_operations (provider_operation_key, outcome)
            VALUES (@key, @outcome)
            ON CONFLICT (provider_operation_key) DO NOTHING
            """, conn))
        {
            cmd.Parameters.AddWithValue("key", providerOperationKey);
            cmd.Parameters.AddWithValue("outcome", effect.ToString());
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // A retry using an existing stable key must see the ORIGINAL provider outcome.
        var existing = await QueryAsync(providerOperationKey, ct)
            ?? throw new InvalidOperationException("Mock PSP did not retain its committed operation.");
        return behavior is MockPspBehavior.TimeoutAfterSuccess ? PaymentState.Unknown : existing;
    }

    /// <summary>
    /// Status inquiry is read-only and does not start or repeat a financial effect.
    /// Null means there is no authoritative provider evidence; NOT a definitive decline.
    /// </summary>
    public async Task<PaymentState?> QueryAsync(string providerOperationKey, CancellationToken ct)
    {
        await using var conn = await source.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            SELECT outcome FROM mockpsp.provider_operations
            WHERE provider_operation_key=@key
            """, conn);
        cmd.Parameters.AddWithValue("key", providerOperationKey);
        var result = await cmd.ExecuteScalarAsync(ct);
        if (result is null or DBNull) return null;
        return Enum.Parse<PaymentState>((string)result);
    }
}
