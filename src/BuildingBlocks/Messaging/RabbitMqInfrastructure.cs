using RabbitMQ.Client;
using System.Text;

namespace PaymentGateway.Messaging;

public static class PaymentEventTopology
{
    public const string Exchange = "paymentgateway.events.v1";
    public const string AuditQueue = "paymentgateway.audit.v1";
    public const string DeadLetterExchange = "paymentgateway.events.dlx";
    public const string DeadLetterQueue = "paymentgateway.audit.dlq";

    public static string Route(string eventType)
    {
        if (!eventType.StartsWith("Payment", StringComparison.Ordinal)
            || eventType.Length <= "Payment".Length)
            throw new ArgumentException("Unknown event type", nameof(eventType));
        return "payment." + eventType["Payment".Length..].ToLowerInvariant();
    }

    public static async Task DeclareAsync(IChannel channel, CancellationToken ct)
    {
        await channel.ExchangeDeclareAsync(
            exchange: Exchange, type: ExchangeType.Topic, durable: true,
            autoDelete: false, cancellationToken: ct);
        await channel.ExchangeDeclareAsync(
            exchange: DeadLetterExchange, type: ExchangeType.Fanout,
            durable: true, autoDelete: false, cancellationToken: ct);
        await channel.QueueDeclareAsync(
            queue: DeadLetterQueue, durable: true, exclusive: false,
            autoDelete: false, arguments: new Dictionary<string, object?>
            {
                ["x-queue-type"] = "quorum"
            }, cancellationToken: ct);
        await channel.QueueBindAsync(
            queue: DeadLetterQueue, exchange: DeadLetterExchange,
            routingKey: "", cancellationToken: ct);
        await channel.QueueDeclareAsync(
            queue: AuditQueue, durable: true, exclusive: false,
            autoDelete: false, arguments: new Dictionary<string, object?>
            {
                ["x-queue-type"] = "quorum",
                ["x-delivery-limit"] = 5,
                ["x-dead-letter-exchange"] = DeadLetterExchange
            }, cancellationToken: ct);
        await channel.QueueBindAsync(
            queue: AuditQueue, exchange: Exchange,
            routingKey: "payment.*", cancellationToken: ct);
    }

    public static async Task<IConnection> ConnectAsync(string uri, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(uri))
            throw new InvalidOperationException("RabbitMq:Uri is required.");
        var factory = new ConnectionFactory
        {
            Uri = new Uri(uri),
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true
        };
        return await factory.CreateConnectionAsync(cancellationToken: ct);
    }

    public static async Task<IChannel> PublisherChannelAsync(IConnection conn, CancellationToken ct)
        => await conn.CreateChannelAsync(
            new CreateChannelOptions(
                publisherConfirmationsEnabled: true,
                publisherConfirmationTrackingEnabled: true),
            ct);
}

/// <summary>
/// Caller owns an exclusive publisher channel. On a confirmed-but-not-marked
/// crash, a replay is expected and is safe for idempotent consumers.
/// </summary>
public sealed class RabbitOutboxPublisher(OutboxStore store, IChannel channel)
{
    public async Task<bool> PublishOneAsync(CancellationToken ct)
    {
        var item = await store.LeaseOneAsync(ct);
        if (item is null) return false;

        try
        {
            var props = new BasicProperties
            {
                Persistent = true,
                ContentType = "application/json",
                MessageId = item.EventId.ToString("D"),
                Type = item.EventType
            };
            await channel.BasicPublishAsync(
                exchange: PaymentEventTopology.Exchange,
                routingKey: PaymentEventTopology.Route(item.EventType),
                mandatory: true, basicProperties: props,
                body: Encoding.UTF8.GetBytes(item.Payload), cancellationToken: ct);
            // BasicPublishAsync is awaited on a channel configured for confirms+tracking.
            // In a rare race, a lease may expire before MarkConfirmed; duplicate is safe.
            await store.MarkConfirmedAsync(item, ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await store.DeferFailedAsync(item, ex.GetType().Name + ": " + ex.Message, ct);
            throw;
        }
    }
}
