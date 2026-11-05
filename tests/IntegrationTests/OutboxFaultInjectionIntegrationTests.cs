using System.Text;
using Npgsql;
using NpgsqlTypes;
using PaymentGateway.Messaging;
using RabbitMQ.Client;
using Xunit;

namespace PaymentGateway.Tests.Integration;

/// <summary>
/// Real PostgreSQL and RabbitMQ where configured, but no live PSP/bank calls.
/// </summary>
public sealed class OutboxFaultInjectionIntegrationTests
{
    private static string? Pg => Environment.GetEnvironmentVariable("PAYMENT_TEST_CONNECTION_STRING");
    private static string? Rabbit => Environment.GetEnvironmentVariable("RABBITMQ_TEST_URI");

    [Fact]
    public async Task PublisherUnavailable_OutboxRemainsUnpublished_AndCanBeLeasedAgain()
    {
        if (string.IsNullOrWhiteSpace(Pg)) return;
        await using var ds = NpgsqlDataSource.Create(Pg);
        var eventId = Guid.NewGuid();
        await InsertOldOutboxAsync(ds, eventId);
        var store = new OutboxStore(ds);

        var first = await store.LeaseOneAsync(CancellationToken.None);
        Assert.NotNull(first);
        Assert.Equal(eventId, first!.EventId);
        // Simulates failed publisher connection; no broker confirmation exists.
        await store.DeferFailedAsync(first, "MockBrokerUnavailable", CancellationToken.None);

        await using var db = await ds.OpenConnectionAsync();
        await using (var check = new NpgsqlCommand("""
            SELECT published_at IS NULL AND last_publish_error='MockBrokerUnavailable'
            FROM messaging.outbox_messages WHERE event_id=@id
            """, db))
        {
            check.Parameters.AddWithValue("id", eventId);
            Assert.True((bool)(await check.ExecuteScalarAsync())!);
        }

        // Accelerate the scheduled retry (no 3s real-time wait).
        await using (var advance = new NpgsqlCommand("""
            UPDATE messaging.outbox_messages SET claim_until=now() - interval '1 second'
            WHERE event_id=@id
            """, db))
        {
            advance.Parameters.AddWithValue("id", eventId);
            await advance.ExecuteNonQueryAsync();
        }
        var retried = await store.LeaseOneAsync(CancellationToken.None);
        Assert.NotNull(retried);
        Assert.Equal(eventId, retried!.EventId);
        Assert.NotEqual(first.ClaimToken, retried.ClaimToken);
    }

    [Fact]
    public async Task PublisherConfirmThenCrashBeforeMark_RedeliversSameEventId_DedupedByInbox()
    {
        if (string.IsNullOrWhiteSpace(Pg) || string.IsNullOrWhiteSpace(Rabbit)) return;
        await using var ds = NpgsqlDataSource.Create(Pg);
        var eventId = Guid.NewGuid();
        await InsertOldOutboxAsync(ds, eventId);
        var store = new OutboxStore(ds);
        var leased = await store.LeaseOneAsync(CancellationToken.None);
        Assert.NotNull(leased);
        Assert.Equal(eventId, leased!.EventId);

        await using var connection = await PaymentEventTopology.ConnectAsync(Rabbit, CancellationToken.None);
        await using var channel = await PaymentEventTopology.PublisherChannelAsync(connection, CancellationToken.None);
        await PaymentEventTopology.DeclareAsync(channel, CancellationToken.None);
        var queue = "test.confirm-gap." + Guid.NewGuid().ToString("N");
        await channel.QueueDeclareAsync(queue: queue, durable: true, exclusive: false,
            autoDelete: false, arguments: null);
        await channel.QueueBindAsync(queue, PaymentEventTopology.Exchange, "payment.requested");

        // First publish is broker-confirmed; intentionally DO NOT mark the DB outbox row.
        await channel.BasicPublishAsync(
            exchange: PaymentEventTopology.Exchange, routingKey: "payment.requested",
            mandatory: true,
            basicProperties: new BasicProperties
            {
                Persistent = true,
                MessageId = eventId.ToString("D"),
                ContentType = "application/json",
                Type = "PaymentRequested"
            },
            body: Encoding.UTF8.GetBytes(leased.Payload));

        await using (var db = await ds.OpenConnectionAsync())
        {
            await using var check = new NpgsqlCommand("""
                SELECT published_at IS NULL FROM messaging.outbox_messages WHERE event_id=@id
                """, db);
            check.Parameters.AddWithValue("id", eventId);
            Assert.True((bool)(await check.ExecuteScalarAsync())!);

            // Simulated restart after the outbox claim has expired.
            await using var expire = new NpgsqlCommand("""
                UPDATE messaging.outbox_messages SET claim_until=now() - interval '1 second'
                WHERE event_id=@id
                """, db);
            expire.Parameters.AddWithValue("id", eventId);
            await expire.ExecuteNonQueryAsync();
        }

        // Outbox publisher replays event after timeout and marks it now.
        var publisher = new RabbitOutboxPublisher(store, channel);
        Assert.True(await publisher.PublishOneAsync(CancellationToken.None));

        var consumer = new InboxProjectionStore(ds);
        var consumerName = "test-confirm-gap-" + Guid.NewGuid().ToString("N");
        var attempts = 0;
        var firstApplied = 0;
        for (var i = 0; i < 50 && attempts < 2; i++)
        {
            var message = await channel.BasicGetAsync(queue, autoAck: false);
            if (message is null)
            {
                await Task.Delay(100);
                continue;
            }
            Assert.Equal(eventId.ToString("D"), message.BasicProperties.MessageId);
            attempts++;
            if (await consumer.ApplyAsync(consumerName, eventId, "PaymentRequested",
                message.Body.ToArray(), CancellationToken.None))
                firstApplied++;
            await channel.BasicAckAsync(message.DeliveryTag, multiple: false);
        }
        Assert.Equal(2, attempts);
        Assert.Equal(1, firstApplied);

        await using var verify = await ds.OpenConnectionAsync();
        await using (var check = new NpgsqlCommand("""
            SELECT count(*) FROM messaging.audit_projection
            WHERE consumer_name=@consumer AND event_id=@id
            """, verify))
        {
            check.Parameters.AddWithValue("consumer", consumerName);
            check.Parameters.AddWithValue("id", eventId);
            Assert.Equal(1L, (long)(await check.ExecuteScalarAsync())!);
        }

        await channel.QueueDeleteAsync(queue);
    }

    private static async Task InsertOldOutboxAsync(NpgsqlDataSource source, Guid eventId)
    {
        await using var db = await source.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO messaging.outbox_messages
                (event_id,aggregate_id,event_type,payload,occurred_at)
            VALUES (@event,@payment,'PaymentRequested',@payload,now() - interval '1000 days')
            """, db);
        cmd.Parameters.AddWithValue("event", eventId);
        cmd.Parameters.AddWithValue("payment", Guid.NewGuid());
        cmd.Parameters.AddWithValue("payload", NpgsqlDbType.Jsonb,
            """{"simulated":true,"fault":"publisher"}""");
        await cmd.ExecuteNonQueryAsync();
    }
}
