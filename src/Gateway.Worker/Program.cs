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
builder.Services.AddHostedService<PaymentProcessingWorker>();
await builder.Build().RunAsync();
