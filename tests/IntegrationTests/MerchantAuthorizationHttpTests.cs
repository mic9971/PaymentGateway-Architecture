using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace PaymentGateway.Tests.Integration;

public sealed class MerchantAuthorizationHttpTests : IClassFixture<MerchantAuthorizationHttpTests.ApiFactory>
{
    private const string Secret="local-ci-demo-jwt-key-not-for-production-2026!";
    private static string? Pg=>Environment.GetEnvironmentVariable("PAYMENT_TEST_CONNECTION_STRING");
    private readonly ApiFactory factory;
    public MerchantAuthorizationHttpTests(ApiFactory app)=>factory=app;

    [Fact]
    public async Task NoToken_WithSpoofedMerchantHeader_IsUnauthorized()
    {
        using var client=factory.CreateClient();
        using var req=new HttpRequestMessage(HttpMethod.Post,"/v1/payments"){
            Content=JsonContent.Create(PaymentBody())};
        req.Headers.Add("Idempotency-Key","auth-key-123");
        req.Headers.Add("X-Demo-Merchant-Id",Guid.NewGuid().ToString("D"));
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.SendAsync(req)).StatusCode);
    }

    [Fact]
    public async Task InvalidExpiryIssuerAudienceAndSignature_AreUnauthorized()
    {
        using var client=factory.CreateClient();
        var id=Guid.NewGuid();
        foreach(var jwt in new[]{
            Token(id,"payments:read",expired:true),
            Token(id,"payments:read",audience:"another.api"),
            Token(id,"payments:read",issuer:"https://evil.example"),
            Forge(Token(id,"payments:read"))
        }){
            using var req=Request(HttpMethod.Get,"/v1/payments/"+Guid.NewGuid(),jwt);
            Assert.Equal(HttpStatusCode.Unauthorized,(await client.SendAsync(req)).StatusCode);
        }
    }

    [Fact]
    public async Task MissingScopeOrMerchantIdentity_ReturnsForbidden()
    {
        using var client=factory.CreateClient();
        using var noScope=Request(HttpMethod.Post,"/v1/payments",
            Token(Guid.NewGuid(),"payments:read"),PaymentBody());
        Assert.Equal(HttpStatusCode.Forbidden,(await client.SendAsync(noScope)).StatusCode);
        using var noMerchant=Request(HttpMethod.Post,"/v1/payments",
            Token(null,"payments:create"),PaymentBody());
        Assert.Equal(HttpStatusCode.Forbidden,(await client.SendAsync(noMerchant)).StatusCode);
    }

    [Fact]
    public async Task PaymentTenantComesFromJwt_NotUntrustedHeader()
    {
        if(Pg is null)return;
        using var client=factory.CreateClient();
        var merchant=Guid.NewGuid();
        var other=Guid.NewGuid();
        using var create=Request(HttpMethod.Post,"/v1/payments",
            Token(merchant,"payments:create"),PaymentBody());
        create.Headers.Add("X-Demo-Merchant-Id",other.ToString("D"));
        var result=await client.SendAsync(create);
        Assert.Equal(HttpStatusCode.Accepted,result.StatusCode);
        using var json=JsonDocument.Parse(await result.Content.ReadAsStringAsync());
        var id=json.RootElement.GetProperty("paymentId").GetGuid();
        using var readMine=Request(HttpMethod.Get,"/v1/payments/"+id,Token(merchant,"payments:read"));
        Assert.Equal(HttpStatusCode.OK,(await client.SendAsync(readMine)).StatusCode);
        using var readOther=Request(HttpMethod.Get,"/v1/payments/"+id,Token(other,"payments:read"));
        Assert.Equal(HttpStatusCode.NotFound,(await client.SendAsync(readOther)).StatusCode);
        using var notAllowed=Request(HttpMethod.Get,"/v1/payments/"+id,Token(merchant,"refunds:read"));
        Assert.Equal(HttpStatusCode.Forbidden,(await client.SendAsync(notAllowed)).StatusCode);
    }

    [Fact]
    public async Task RefundRequiresOwnScope_AndDeniesOtherMerchant()
    {
        if(Pg is null)return;
        using var client=factory.CreateClient();
        var merchant=Guid.NewGuid();
        var other=Guid.NewGuid();
        using var payRequest=Request(HttpMethod.Post,"/v1/payments",
            Token(merchant,"payments:create"),PaymentBody());
        var payResponse=await client.SendAsync(payRequest);
        Assert.Equal(HttpStatusCode.Accepted,payResponse.StatusCode);
        using var payJson=JsonDocument.Parse(await payResponse.Content.ReadAsStringAsync());
        var paymentId=payJson.RootElement.GetProperty("paymentId").GetGuid();

        // Local test fixture only: no real bank settlement or PSP call.
        await using(var source=NpgsqlDataSource.Create(Pg))
        await using(var conn=await source.OpenConnectionAsync())
        await using(var cmd=new NpgsqlCommand("""
            UPDATE payment.payment_intents SET state='Succeeded',version=version+1
            WHERE payment_id=@payment AND merchant_id=@merchant
            """,conn)){
            cmd.Parameters.AddWithValue("payment",paymentId);
            cmd.Parameters.AddWithValue("merchant",merchant);
            Assert.Equal(1,await cmd.ExecuteNonQueryAsync());
        }
        var url=$"/v1/payments/{paymentId}/refunds";
        using var paymentOnly=Request(HttpMethod.Post,url,
            Token(merchant,"payments:create payments:read"),new{amountMinor=25000});
        Assert.Equal(HttpStatusCode.Forbidden,(await client.SendAsync(paymentOnly)).StatusCode);
        using var otherRefund=Request(HttpMethod.Post,url,
            Token(other,"refunds:create"),new{amountMinor=25000});
        Assert.Equal(HttpStatusCode.Conflict,(await client.SendAsync(otherRefund)).StatusCode);
        using var goodRefund=Request(HttpMethod.Post,url,
            Token(merchant,"refunds:create"),new{amountMinor=25000});
        var reply=await client.SendAsync(goodRefund);
        Assert.Equal(HttpStatusCode.Accepted,reply.StatusCode);
        using var refundJson=JsonDocument.Parse(await reply.Content.ReadAsStringAsync());
        var refundId=refundJson.RootElement.GetProperty("refundId").GetGuid();
        using var forbiddenRead=Request(HttpMethod.Get,"/v1/refunds/"+refundId,
            Token(other,"refunds:read"));
        Assert.Equal(HttpStatusCode.NotFound,(await client.SendAsync(forbiddenRead)).StatusCode);
        using var allowedRead=Request(HttpMethod.Get,"/v1/refunds/"+refundId,
            Token(merchant,"refunds:read"));
        Assert.Equal(HttpStatusCode.OK,(await client.SendAsync(allowedRead)).StatusCode);
    }

    [Fact]
    public async Task HealthIsPublic_ButMockWebhookNeedsItsOwnHmac()
    {
        using var client=factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK,(await client.GetAsync("/health/live")).StatusCode);
        using var callback=new HttpRequestMessage(HttpMethod.Post,"/v1/providers/mock/webhook"){
            Content=JsonContent.Create(new{status="Succeeded"})};
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.SendAsync(callback)).StatusCode);
    }

    private static object PaymentBody()=>new{
        merchantReference="auth-"+Guid.NewGuid().ToString("N"),
        amountMinor=100000,currency="VND",paymentMethod="MOCK"
    };

    private static HttpRequestMessage Request(HttpMethod method,string url,string jwt,object? body=null)
    {
        var request=new HttpRequestMessage(method,url);
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",jwt);
        if(method==HttpMethod.Post){
            request.Headers.Add("Idempotency-Key","auth-"+Guid.NewGuid().ToString("N"));
            request.Content=JsonContent.Create(body);
        }
        return request;
    }

    private static string Forge(string jwt)
    {
        var parts=jwt.Split('.');
        parts[2]=(parts[2][0]=='A'?"B":"A")+parts[2][1..];
        return string.Join('.',parts);
    }

    private static string Token(Guid? merchant,string scope,bool expired=false,
        string issuer="https://merchant-auth.local.test",
        string audience="paymentgateway.mock.api")
    {
        static string B64(byte[] bytes)=>Convert.ToBase64String(bytes)
            .TrimEnd('=').Replace('+','-').Replace('/','_');
        var now=DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var claims=new Dictionary<string,object>{
            ["iss"]=issuer,["aud"]=audience,["sub"]="local-merchant-client",
            ["iat"]=now-120,["nbf"]=now-120,["exp"]=now+(expired?-30:600),
            ["jti"]=Guid.NewGuid().ToString("D"),["scope"]=scope
        };
        if(merchant.HasValue)claims["merchant_id"]=merchant.Value.ToString("D");
        var input=B64(Encoding.UTF8.GetBytes("""{"alg":"HS256","typ":"JWT"}"""))+
            "."+B64(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(claims)));
        return input+"."+B64(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(Secret),Encoding.UTF8.GetBytes(input)));
    }

    public sealed class ApiFactory:WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_,cfg)=>cfg.AddInMemoryCollection(
                new Dictionary<string,string?>{
                    ["MerchantAuth:DevSigningKey"]=Secret,
                    ["MockPsp:WebhookSecret"]="ci-local-webhook-secret-not-real",
                    ["ConnectionStrings:Payments"]=Pg ??
                        "Host=localhost;Database=unused;Username=unused;Password=unused"
                }));
        }
    }
}
