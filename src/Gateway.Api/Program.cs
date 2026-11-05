using Npgsql;
using PaymentGateway.Payments.Application;
using PaymentGateway.Payments.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// SECURITY BOUNDARY: This tutorial uses a deliberately fake local merchant identity.
// Do not allow startup outside Development until real merchant authentication is implemented.
if (!builder.Environment.IsDevelopment())
    throw new InvalidOperationException("Demo-only API. Configure real merchant authentication before non-development deployment.");

var connectionString = builder.Configuration.GetConnectionString("Payments")
    ?? throw new InvalidOperationException("ConnectionStrings:Payments must be configured.");

builder.Services.AddSingleton(NpgsqlDataSource.Create(connectionString));
builder.Services.AddScoped<IPaymentStore, NpgsqlPaymentStore>();
builder.Services.AddScoped<PaymentService>();
builder.Services.AddScoped<NpgsqlMockWebhookProcessor>();
var webhookSecret = builder.Configuration["MockPsp:WebhookSecret"]
    ?? throw new InvalidOperationException("Development-only MockPsp:WebhookSecret is required.");
if (System.Text.Encoding.UTF8.GetByteCount(webhookSecret) < 16)
    throw new InvalidOperationException("MockPsp:WebhookSecret must contain at least 16 UTF-8 bytes.");
builder.Services.AddSingleton(new MockWebhookSecret(System.Text.Encoding.UTF8.GetBytes(webhookSecret)));

var app = builder.Build();

app.MapGet("/health/live", () => Results.Ok(new { status = "live", demo = true }));

app.MapPost("/v1/payments", async (
    HttpContext http, CreatePaymentRequest request, PaymentService service, CancellationToken ct) =>
{
    if (!TryGetLocalDemoMerchant(http, out var merchantId))
        return Results.Unauthorized();

    var key = http.Request.Headers["Idempotency-Key"].ToString();
    try
    {
        var result = await service.CreateAsync(
            new CreatePaymentCommand(merchantId, key, request.MerchantReference,
                request.AmountMinor, request.Currency, request.PaymentMethod), ct);
        var payment = result.Payment;
        var response = new PaymentResponse(
            payment.PaymentId, payment.MerchantReference, payment.AmountMinor,
            payment.Currency, payment.PaymentMethod, payment.State.ToString(), result.Replayed);
        return Results.Json(response, statusCode: result.Replayed ? StatusCodes.Status200OK : StatusCodes.Status202Accepted);
    }
    catch (IdempotencyConflictException e)
    {
        return Results.Conflict(new { error = e.Message });
    }
    catch (ArgumentException e)
    {
        return Results.BadRequest(new { error = e.Message });
    }
});

app.MapGet("/v1/payments/{paymentId:guid}", async (
    HttpContext http, Guid paymentId, PaymentService service, CancellationToken ct) =>
{
    if (!TryGetLocalDemoMerchant(http, out var merchantId))
        return Results.Unauthorized();
    var payment = await service.GetAsync(merchantId, paymentId, ct);
    return payment is null
        ? Results.NotFound()
        : Results.Ok(new PaymentResponse(payment.PaymentId, payment.MerchantReference,
            payment.AmountMinor, payment.Currency, payment.PaymentMethod, payment.State.ToString(), false));
});


app.MapPost("/v1/providers/mock/webhook", async (
    HttpContext http, NpgsqlMockWebhookProcessor processor,
    MockWebhookSecret secret, CancellationToken ct) =>
{
    // Development-only fake PSP signature protocol. No real provider is connected.
    if (http.Request.ContentLength > 16_384)
        return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
    using var ms = new MemoryStream();
    var buffer = new byte[4096];
    while (true)
    {
        var count = await http.Request.Body.ReadAsync(buffer.AsMemory(), ct);
        if (count == 0) break;
        if (ms.Length + count > 16_384)
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        ms.Write(buffer, 0, count);
    }
    var raw = ms.ToArray();
    var timestamp = http.Request.Headers["X-Mock-Timestamp"].ToString();
    var signature = http.Request.Headers["X-Mock-Signature"].ToString();
    if (!MockWebhookSignature.Verify(secret.Value, timestamp, signature, raw, DateTimeOffset.UtcNow))
        return Results.Unauthorized();

    MockSignedWebhook payload;
    try { payload = NpgsqlMockWebhookProcessor.Deserialize(raw); }
    catch (System.Text.Json.JsonException) { return Results.BadRequest(new { error = "Invalid webhook JSON." }); }

    var result = await processor.ApplyVerifiedAsync(payload, raw, ct);
    return result switch
    {
        MockWebhookDisposition.InvalidCorrelation => Results.BadRequest(new { error = "Invalid provider correlation." }),
        MockWebhookDisposition.ConflictingFinal => Results.Accepted(value: new { disposition = "ConflictingFinal", investigation_required = true }),
        _ => Results.Ok(new { disposition = result.ToString() })
    };
});

app.Run();

static bool TryGetLocalDemoMerchant(HttpContext http, out Guid merchantId)
{
    // NOT authentication. Enabled ONLY under Development startup guard and local port binding.
    var raw = http.Request.Headers["X-Demo-Merchant-Id"].ToString();
    return Guid.TryParse(raw, out merchantId) && merchantId != Guid.Empty;
}

public sealed record CreatePaymentRequest(string MerchantReference, long AmountMinor, string Currency, string PaymentMethod);
public sealed record PaymentResponse(
    Guid PaymentId, string MerchantReference, long AmountMinor, string Currency,
    string PaymentMethod, string Status, bool Replayed);


public sealed record MockWebhookSecret(byte[] Value);
