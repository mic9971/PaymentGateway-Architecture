using Npgsql;
using PaymentGateway.Messaging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text.Json;

namespace PaymentGateway.Worker;

/// <summary>
/// A demonstrator consumer: stores an idempotent audit projection only.
/// It never triggers a PSP operation or changes the Payment aggregate.
/// </summary>
public sealed class AuditProjectionConsumerHostedService(
    NpgsqlDataSource dataSource, IConfiguration config,
    ILogger<AuditProjectionConsumerHostedService> log) : BackgroundService
{
    public const string ConsumerName = "demo-payment-audit-v1";

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var store = new InboxProjectionStore(dataSource);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var connection = await PaymentEventTopology.ConnectAsync(
                    config["RabbitMq:Uri"] ?? "", ct);
                await using var channel = await connection.CreateChannelAsync(cancellationToken: ct);
                await PaymentEventTopology.DeclareAsync(channel, ct);
                await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false,
                    cancellationToken: ct);
                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.ReceivedAsync += async (_, args) =>
                {
                    // RabbitMQ.Client v7 body memory is invalid once callback returns.
                    var bytes = args.Body.ToArray();
                    try
                    {
                        if (!Guid.TryParse(args.BasicProperties.MessageId, out var eventId))
                            throw new ArgumentException("Missing or invalid event id.");
                        var eventType = args.BasicProperties.Type ?? "";
                        var first = await store.ApplyAsync(
                            ConsumerName, eventId, eventType, bytes, ct);
                        if (!first)
                            log.LogDebug("Deduplicated payment event {EventId}", eventId);
                        // Ack AFTER the same PostgreSQL TX commits both inbox and projection.
                        await channel.BasicAckAsync(args.DeliveryTag, multiple: false, cancellationToken: ct);
                    }
                    catch (Exception ex) when (ex is JsonException or ArgumentException)
                    {
                        log.LogError(ex, "Malformed internal payment event; dead-lettering");
                        await channel.BasicRejectAsync(args.DeliveryTag, requeue: false, cancellationToken: ct);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        // No ack: RabbitMQ redelivers after connection closes.
                    }
                    catch (Exception ex)
                    {
                        log.LogWarning(ex, "Transient inbox processing failure; requesting redelivery");
                        // Quorum queue delivery-limit sends repeated failures to DLQ.
                        await channel.BasicNackAsync(
                            args.DeliveryTag, multiple: false, requeue: true, cancellationToken: ct);
                    }
                };
                await channel.BasicConsumeAsync(
                    queue: PaymentEventTopology.AuditQueue, autoAck: false,
                    consumer: consumer, cancellationToken: ct);
                // The RabbitMQ client automatically recovers network connections/subscriptions.
                // Non-recoverable channel errors require a worker restart (operational alert).
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "RabbitMQ consumer unavailable; reconnecting");
                try { await Task.Delay(TimeSpan.FromSeconds(2), ct); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            }
        }
    }
}
