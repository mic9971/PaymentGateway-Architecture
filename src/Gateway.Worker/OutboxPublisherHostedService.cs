using Npgsql;
using PaymentGateway.Messaging;

namespace PaymentGateway.Worker;

/// <summary>Dedicated publisher owns its own channel: no concurrent publish on a shared channel.</summary>
public sealed class OutboxPublisherHostedService(
    NpgsqlDataSource dataSource, IConfiguration config,
    ILogger<OutboxPublisherHostedService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var store = new OutboxStore(dataSource);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var connection = await PaymentEventTopology.ConnectAsync(
                    config["RabbitMq:Uri"] ?? "", ct);
                await using var channel = await PaymentEventTopology.PublisherChannelAsync(connection, ct);
                await PaymentEventTopology.DeclareAsync(channel, ct);
                var publisher = new RabbitOutboxPublisher(store, channel);
                while (!ct.IsCancellationRequested)
                {
                    if (!await publisher.PublishOneAsync(ct))
                        await Task.Delay(TimeSpan.FromMilliseconds(250), ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // PostgreSQL outbox survives broker outages; reconnect and republish later.
                log.LogWarning(ex, "RabbitMQ publisher unavailable; outbox records retained");
                try { await Task.Delay(TimeSpan.FromSeconds(2), ct); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            }
        }
    }
}
