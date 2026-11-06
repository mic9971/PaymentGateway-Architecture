using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using PaymentGateway.Payments.Application;
using PaymentGateway.Payments.Infrastructure;
using PaymentGateway.Refunds.Application;
using PaymentGateway.Refunds.Domain;
using PaymentGateway.Refunds.Infrastructure;
using Xunit;

namespace PaymentGateway.Tests.Integration;

/// <summary>
/// Real HTTP/JWT pipeline + PostgreSQL. All identities and money are synthetic.
/// This does not prove real staff identities or an externally connected OIDC IdP.
/// </summary>
public sealed class FinanceApprovalHttpTests :
    IClassFixture<MerchantAuthorizationHttpTests.ApiFactory>
{
    private const string JwtKey = "local-ci-demo-jwt-key-not-for-production-2026!";
    private const string Issuer = "https://merchant-auth.local.test";
    private const string Audience = "paymentgateway.mock.api";
    private static string? Pg => Environment.GetEnvironmentVariable("PAYMENT_TEST_CONNECTION_STRING");
    private readonly MerchantAuthorizationHttpTests.ApiFactory factory;
    public FinanceApprovalHttpTests(MerchantAuthorizationHttpTests.ApiFactory f) => factory = f;

    [Fact]
    public async Task ApprovalHttp_RequiresJWTAndFinanceScope_ThenReleasesOnlyAfterTwoActors()
    {
        if(Pg is null)return;
        await using var db=NpgsqlDataSource.Create(Pg);
        var merchant=Guid.NewGuid();
        var pay=new PaymentService(new NpgsqlPaymentStore(db));
        var created=await pay.CreateAsync(new CreatePaymentCommand(
            merchant,"http-approval-"+Guid.NewGuid().ToString("N"),
            "approval-case",2_000_000,"VND","MOCK"),CancellationToken.None);
        Guid attemptId;
        string key;
        await using(var conn=await db.OpenConnectionAsync())
        await using(var query=new NpgsqlCommand("""
            SELECT attempt_id,provider_operation_key FROM payment.payment_attempts
            WHERE payment_id=@payment
            """,conn))
        {
            query.Parameters.AddWithValue("payment",created.Payment.PaymentId);
            await using var rd=await query.ExecuteReaderAsync();
            Assert.True(await rd.ReadAsync());
            attemptId=rd.GetGuid(0);
            key=rd.GetString(1);
        }
        var result=new MockSignedWebhook(Guid.NewGuid(),created.Payment.PaymentId,
            attemptId,key,2_000_000,"VND","Succeeded");
        Assert.Equal(MockWebhookDisposition.Applied,
            await new NpgsqlMockWebhookProcessor(db).ApplyVerifiedAsync(result,
                Encoding.UTF8.GetBytes(JsonSerializer.Serialize(result)),CancellationToken.None));
        var refund=await new RefundService(new NpgsqlRefundStore(db)).RequestAsync(
            new RequestRefundCommand(merchant,created.Payment.PaymentId,
                "http-two-person-"+Guid.NewGuid().ToString("N"),1_200_000,
                "requester-person","requester-client"),CancellationToken.None);
        Assert.Equal(RefundState.PendingApproval,refund.Refund.State);

        await RegisterActorAsync(db,merchant,"finance-a","finance-client-a");
        await RegisterActorAsync(db,merchant,"finance-b","finance-client-b");
        var url=$"/v1/refunds/{refund.Refund.RefundId}/approvals";
        using var http=factory.CreateClient();

        using(var noJwt=new HttpRequestMessage(HttpMethod.Post,url))
            Assert.Equal(HttpStatusCode.Unauthorized,(await http.SendAsync(noJwt)).StatusCode);
        using(var noScope=ApprovalRequest(url,Token(merchant,"finance-a",
            "finance-client-a","refunds:read")))
            Assert.Equal(HttpStatusCode.Forbidden,(await http.SendAsync(noScope)).StatusCode);

        using(var wrongTenant=ApprovalRequest(url,Token(Guid.NewGuid(),
            "finance-a","finance-client-a","refunds:approve")))
            Assert.Equal(HttpStatusCode.Forbidden,(await http.SendAsync(wrongTenant)).StatusCode);

        using(var first=ApprovalRequest(url,Token(merchant,"finance-a",
            "finance-client-a","refunds:approve")))
            Assert.Equal(HttpStatusCode.Accepted,(await http.SendAsync(first)).StatusCode);

        // First approval must NOT release the Refund to the worker.
        Assert.Null(await new NpgsqlRefundProcessing(db).ClaimRefundAsync(
            refund.Refund.RefundId,CancellationToken.None));

        using(var repeated=ApprovalRequest(url,Token(merchant,"finance-a",
            "finance-client-a","refunds:approve")))
            Assert.Equal(HttpStatusCode.Conflict,(await http.SendAsync(repeated)).StatusCode);

        using(var second=ApprovalRequest(url,Token(merchant,"finance-b",
            "finance-client-b","refunds:approve")))
            Assert.Equal(HttpStatusCode.OK,(await http.SendAsync(second)).StatusCode);

        var current=await new RefundService(new NpgsqlRefundStore(db))
            .GetAsync(merchant,refund.Refund.RefundId,CancellationToken.None);
        Assert.Equal(RefundState.Requested,current!.State);
    }

    private static HttpRequestMessage ApprovalRequest(string url,string jwt)
    {
        var message=new HttpRequestMessage(HttpMethod.Post,url);
        message.Headers.Authorization=new AuthenticationHeaderValue("Bearer",jwt);
        return message;
    }

    private static string Token(Guid merchant,string subject,string clientId,string scope)
    {
        static string B64(byte[] bytes)=>Convert.ToBase64String(bytes)
            .TrimEnd('=').Replace('+','-').Replace('/','_');
        var now=DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var claims=new Dictionary<string,object>{
            ["iss"]=Issuer,["aud"]=Audience,
            ["sub"]=subject,["client_id"]=clientId,
            ["merchant_id"]=merchant.ToString("D"),["scope"]=scope,
            ["iat"]=now-20,["nbf"]=now-20,["exp"]=now+600,
            ["jti"]=Guid.NewGuid().ToString("D")
        };
        var content=B64(Encoding.UTF8.GetBytes("""{"alg":"HS256","typ":"JWT"}"""))+
            "."+B64(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(claims)));
        return content+"."+B64(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(JwtKey),Encoding.UTF8.GetBytes(content)));
    }

    private static async Task RegisterActorAsync(NpgsqlDataSource db,Guid merchant,
        string subject,string clientId)
    {
        await using var conn=await db.OpenConnectionAsync();
        await using var tx=await conn.BeginTransactionAsync();
        await using(var m=new NpgsqlCommand("""
            INSERT INTO iam.merchants(merchant_id,status)
            VALUES(@id,'Active') ON CONFLICT(merchant_id)
            DO UPDATE SET status='Active'
            """,conn,tx))
        {
            m.Parameters.AddWithValue("id",merchant);
            await m.ExecuteNonQueryAsync();
        }
        await using(var a=new NpgsqlCommand("""
            INSERT INTO iam.merchant_clients
            (merchant_id,issuer,subject,client_id,granted_scopes,enabled)
            VALUES(@id,@issuer,@subject,@client,ARRAY['refunds:approve']::text[],true)
            """,conn,tx))
        {
            a.Parameters.AddWithValue("id",merchant);
            a.Parameters.AddWithValue("issuer",Issuer);
            a.Parameters.AddWithValue("subject",subject);
            a.Parameters.AddWithValue("client",clientId);
            await a.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
    }
}
