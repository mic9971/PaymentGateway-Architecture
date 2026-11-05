using Npgsql;
using PaymentGateway.Payments.Infrastructure;
using PaymentGateway.Worker;

var builder = Host.CreateApplicationBuilder(args);
if (!builder.Environment.IsDevelopment())
    throw new InvalidOperationException("Mock PSP worker is Development-only.");
var connection = builder.Configuration.GetConnectionString("Payments")
    ?? throw new InvalidOperationException("ConnectionStrings:Payments must be configured.");

builder.Services.AddSingleton(NpgsqlDataSource.Create(connection));
builder.Services.AddSingleton<NpgsqlPaymentWorkStore>();
builder.Services.AddSingleton<MockPsp>();

// DB-polling payment attempt worker remains the ONLY demo process allowed to call Mock PSP.
// RabbitMQ subscriber below is an idempotent audit projection, NOT a charge initiator.
builder.Services.AddHostedService<PaymentProcessingWorker>();
builder.Services.AddHostedService<OutboxPublisherHostedService>();
builder.Services.AddHostedService<AuditProjectionConsumerHostedService>();
await builder.Build().RunAsync();
