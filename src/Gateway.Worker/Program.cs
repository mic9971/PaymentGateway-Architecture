using Npgsql;
using PaymentGateway.Payments.Infrastructure;
using PaymentGateway.Refunds.Infrastructure;
using PaymentGateway.Worker;

var builder = Host.CreateApplicationBuilder(args);
if (!builder.Environment.IsDevelopment())
    throw new InvalidOperationException("Mock PSP worker is Development-only.");
var connection = builder.Configuration.GetConnectionString("Payments")
    ?? throw new InvalidOperationException("ConnectionStrings:Payments must be configured.");

builder.Services.AddSingleton(NpgsqlDataSource.Create(connection));
builder.Services.AddSingleton<NpgsqlPaymentWorkStore>();
builder.Services.AddSingleton<NpgsqlUnknownRecoveryStore>();
builder.Services.AddSingleton<NpgsqlMockPspLedger>();
builder.Services.AddSingleton<PaymentRecoveryService>();
builder.Services.AddSingleton<NpgsqlMockReconciliationStore>();
builder.Services.AddSingleton<NpgsqlRefundProcessing>();
builder.Services.AddSingleton<NpgsqlMockRefundProvider>();
builder.Services.AddSingleton<NpgsqlRefundRecovery>();

// Only DB-polling attempt worker may invoke Mock PSP CreatePayment.
// The RabbitMQ consumer remains a non-financial audit projection.
builder.Services.AddHostedService<PaymentProcessingWorker>();
builder.Services.AddHostedService<PaymentRecoveryWorker>();
builder.Services.AddHostedService<RefundWorker>();
builder.Services.AddHostedService<MockReconciliationWorker>();
builder.Services.AddHostedService<OutboxPublisherHostedService>();
builder.Services.AddHostedService<AuditProjectionConsumerHostedService>();
await builder.Build().RunAsync();
