using Npgsql;

namespace PaymentGateway.Messaging;

/// <summary>
/// A lease is a coordination tool. If it expires during publishing the event can be
/// published twice. EventId remains stable so consumers can durably deduplicate.
/// </summary>
public sealed record LeasedOutboxEvent(
    Guid EventId, Guid AggregateId, string EventType, string Payload,
    Guid ClaimToken);

public sealed class OutboxStore(NpgsqlDataSource dataSource)
{
    public async Task<LeasedOutboxEvent?> LeaseOneAsync(CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            WITH picked AS (
                SELECT event_id FROM messaging.outbox_messages
                WHERE published_at IS NULL
                  AND (claim_until IS NULL OR claim_until < now())
                ORDER BY occurred_at, event_id
                LIMIT 1 FOR UPDATE SKIP LOCKED
            )
            UPDATE messaging.outbox_messages o
            SET claim_until = now() + interval '30 seconds',
                claim_token = @token,
                publish_attempts = o.publish_attempts + 1
            FROM picked
            WHERE o.event_id = picked.event_id
            RETURNING o.event_id, o.aggregate_id, o.event_type, o.payload::text
            """, conn);
        var token = Guid.NewGuid();
        cmd.Parameters.AddWithValue("token", token);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new LeasedOutboxEvent(
            reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2),
            reader.GetString(3), token);
    }

    public async Task<bool> MarkConfirmedAsync(LeasedOutboxEvent item, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            UPDATE messaging.outbox_messages
            SET published_at=now(), claim_until=NULL, claim_token=NULL, last_publish_error=NULL
            WHERE event_id=@id AND claim_token=@token AND published_at IS NULL
            """, conn);
        cmd.Parameters.AddWithValue("id", item.EventId);
        cmd.Parameters.AddWithValue("token", item.ClaimToken);
        return await cmd.ExecuteNonQueryAsync(ct) == 1;
    }

    public async Task DeferFailedAsync(LeasedOutboxEvent item, string reason, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            UPDATE messaging.outbox_messages
            SET claim_until=now() + interval '3 seconds',
                claim_token=NULL, last_publish_error=@reason
            WHERE event_id=@id AND claim_token=@token AND published_at IS NULL
            """, conn);
        cmd.Parameters.AddWithValue("id", item.EventId);
        cmd.Parameters.AddWithValue("token", item.ClaimToken);
        cmd.Parameters.AddWithValue("reason", reason[..Math.Min(500, reason.Length)]);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
