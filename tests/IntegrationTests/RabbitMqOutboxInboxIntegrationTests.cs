using System.Text;
using Npgsql;
using NpgsqlTypes;
using PaymentGateway.Messaging;
using RabbitMQ.Client;
using Xunit;

namespace PaymentGateway.Tests.Integration;

/// <summary>
/// Real PostgreSQL and RabbitMQ integration exercises. CI sets both environment variables.
/// Without both services configured locally, individual tests return without executing.
/// </summary>
public sealed class RabbitMqOutboxInboxIntegrationTests
{
    private static string? Pg => Environment.GetEnvironmentVariable("PAYMENT_TEST_CONNECTION_STRING");
    private static string? Rabbit => Environment.GetEnvironmentVariable("RABBITMQ_TEST_URI");

    [Fact]
    public async Task Inbox_ConcurrentDuplicateEvent_ProjectsOnlyOnce()
    {
        if (string.IsNullOrWhiteSpace(Pg)) return;
        await using var source = NpgsqlDataSource.Create(Pg);
        var inbox = new InboxProjectionStore(source);
        var eventId = Guid.NewGuid();
        var consumer = "integration-" + Guid.NewGuid().ToString("N");
        var payload = Encoding.UTF8.GetBytes("{\"payment_id\":\"test\",\"simulated\":true}");
        var applied = await Task.WhenAll(Enumerable.Range(0, 12)
            .Select(_ => inbox.ApplyAsync(consumer, eventId, "PaymentRequested", payload, CancellationToken.None)));
        Assert.Equal(1, applied.Count(x => x));
        Assert.Equal(11, applied.Count(x => !x));

        await using var conn = await source.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT count(*) FROM messaging.audit_projection
            WHERE consumer_name=@consumer AND event_id=@event
            """, conn);
        cmd.Parameters.AddWithValue("consumer", consumer);
        cmd.Parameters.AddWithValue("event", eventId);
        Assert.Equal(1L, (long)(await cmd.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task Inbox_InvalidJson_RollsBackReceipt()
    {
        if (string.IsNullOrWhiteSpace(Pg)) return;
        await using var source = NpgsqlDataSource.Create(Pg);
        var inbox = new InboxProjectionStore(source);
        var eventId = Guid.NewGuid();
        var consumer = "badjson-" + Guid.NewGuid().ToString("N");
        await Assert.ThrowsAsync<System.Text.Json.JsonException>(() =>
            inbox.ApplyAsync(consumer, eventId, "PaymentRequested",
                Encoding.UTF8.GetBytes("{invalid-json"), CancellationToken.None));

        await using var conn = await source.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT count(*) FROM messaging.consumer_inbox
            WHERE consumer_name=@consumer AND event_id=@event
            """, conn);
        cmd.Parameters.AddWithValue("consumer", consumer);
        cmd.Parameters.AddWithValue("event", eventId);
        Assert.Equal(0L, (long)(await cmd.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task RabbitMq_ConfirmedPublish_FromDurableOutbox_ReachesQueue()
    {
        if (string.IsNullOrWhiteSpace(Pg) || string.IsNullOrWhiteSpace(Rabbit)) return;
        await using var source = NpgsqlDataSource.Create(Pg);
        var eventId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        await using (var db = await source.OpenConnectionAsync())
        {
            await using var insert = new NpgsqlCommand("""
                INSERT INTO messaging.outbox_messages (event_id,aggregate_id,event_type,payload,occurred_at)
                VALUES (@event,@payment,'PaymentRequested',@payload,now())
                """, db);
            insert.Parameters.AddWithValue("event", eventId);
            insert.Parameters.AddWithValue("payment", paymentId);
            insert.Parameters.AddWithValue("payload", NpgsqlDbType.Jsonb,
                "{\"payment_id\":\"" + paymentId + "\",\"simulated\":true}");
            await insert.ExecuteNonQueryAsync();
        }

        await using var connection = await PaymentEventTopology.ConnectAsync(Rabbit, CancellationToken.None);
        await using var channel = await PaymentEventTopology.PublisherChannelAsync(connection, CancellationToken.None);
        await PaymentEventTopology.DeclareAsync(channel, CancellationToken.None);
        var publisher = new RabbitOutboxPublisher(new OutboxStore(source), channel);

        // Earlier tests may have left unpublished outbox rows; process them too.
        for (var i = 0; i < 100; i++)
        {
            await publisher.PublishOneAsync(CancellationToken.None);
            await using var db = await source.OpenConnectionAsync();
            await using var check = new NpgsqlCommand("""
                SELECT published_at IS NOT NULL FROM messaging.outbox_messages WHERE event_id=@event
                """, db);
            check.Parameters.AddWithValue("event", eventId);
            if ((bool)(await check.ExecuteScalarAsync())!) break;
        }

        await using (var db = await source.OpenConnectionAsync())
        {
            await using var check = new NpgsqlCommand("""
                SELECT published_at IS NOT NULL FROM messaging.outbox_messages WHERE event_id=@event
                """, db);
            check.Parameters.AddWithValue("event", eventId);
            Assert.True((bool)(await check.ExecuteScalarAsync())!);
        }

        // The queue receives the stable event ID. We ack other pending demo events as we drain.
        var delivered = false;
        for (var i = 0; i < 150; i++)
        {
            var message = await channel.BasicGetAsync(PaymentEventTopology.AuditQueue, autoAck: false);
            if (message is null)
            {
                await Task.Delay(100);
                continue;
            }
            if (message.BasicProperties.MessageId == eventId.ToString("D"))
                delivered = true;
            await channel.BasicAckAsync(message.DeliveryTag, multiple: false);
            if (delivered) break;
        }
        Assert.True(delivered);
    }
}
