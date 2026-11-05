using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace PaymentGateway.Messaging;

/// <summary>
/// Technical demonstration of an idempotent consumer. This is an audit-style
/// read projection only; it never invokes PSPs or changes Payment domain state.
/// </summary>
public sealed class InboxProjectionStore(NpgsqlDataSource dataSource)
{
    public async Task<bool> ApplyAsync(
        string consumerName, Guid eventId, string eventType, byte[] body, CancellationToken ct)
    {
        if (eventId == Guid.Empty || string.IsNullOrWhiteSpace(consumerName)
            || !eventType.StartsWith("Payment", StringComparison.Ordinal)
            || body.Length is <= 0 or > 65536)
            throw new ArgumentException("Malformed internal event metadata or size.");
        using var verifiedPayload = JsonDocument.Parse(body); // reject invalid JSON before insert
        if (verifiedPayload.RootElement.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Expected event JSON object.");

        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var claim = new NpgsqlCommand("""
            INSERT INTO messaging.consumer_inbox (consumer_name, event_id, received_at)
            VALUES (@consumer,@event,now())
            ON CONFLICT DO NOTHING RETURNING event_id
            """, conn, tx);
        claim.Parameters.AddWithValue("consumer", consumerName);
        claim.Parameters.AddWithValue("event", eventId);
        var first = await claim.ExecuteScalarAsync(ct);
        if (first is null or DBNull)
        {
            await tx.CommitAsync(ct);
            return false;
        }

        await using var projection = new NpgsqlCommand("""
            INSERT INTO messaging.audit_projection
                (consumer_name, event_id, event_type, payload, received_at)
            VALUES (@consumer,@event,@type,@payload,now())
            """, conn, tx);
        projection.Parameters.AddWithValue("consumer", consumerName);
        projection.Parameters.AddWithValue("event", eventId);
        projection.Parameters.AddWithValue("type", eventType);
        projection.Parameters.AddWithValue("payload", NpgsqlDbType.Jsonb, System.Text.Encoding.UTF8.GetString(body));
        await projection.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }
}
