using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Npgsql;

namespace PaymentGateway.Gateway.Api.Security;

public sealed record MerchantOperationRequirement(string Operation) : IAuthorizationRequirement;

public sealed class MerchantAccessStore(NpgsqlDataSource source)
{
    /// <summary>Fail closed: grant only if merchant active, client enabled and scope persisted.</summary>
    public async Task<bool> IsAllowedAsync(
        Guid merchantId, string issuer, string subject, string clientId,
        string operation, CancellationToken ct)
    {
        await using var conn = await source.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            SELECT EXISTS(
                SELECT 1
                FROM iam.merchants m
                JOIN iam.merchant_clients c ON c.merchant_id=m.merchant_id
                WHERE m.merchant_id=@merchant AND m.status='Active'
                  AND c.enabled
                  AND c.issuer=@issuer AND c.subject=@subject AND c.client_id=@client
                  AND @operation=ANY(c.granted_scopes)
            )
            """, conn);
        cmd.Parameters.AddWithValue("merchant",merchantId);
        cmd.Parameters.AddWithValue("issuer",issuer);
        cmd.Parameters.AddWithValue("subject",subject);
        cmd.Parameters.AddWithValue("client",clientId);
        cmd.Parameters.AddWithValue("operation",operation);
        return (bool)(await cmd.ExecuteScalarAsync(ct) ?? false);
    }
}

public sealed class MerchantOperationHandler(
    MerchantAccessStore store, ILogger<MerchantOperationHandler> logger)
    : AuthorizationHandler<MerchantOperationRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, MerchantOperationRequirement requirement)
    {
        if (requirement.Operation == "__deny_unclassified__" ||
            !MerchantJwtAuthentication.TryGetMerchantId(context.User,out var merchant))
            return;

        var issuer=context.User.FindFirst("iss")?.Value;
        var subject=context.User.FindFirst("sub")?.Value;
        var clientId=context.User.FindFirst("client_id")?.Value;
        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject)
            || string.IsNullOrWhiteSpace(clientId)) return;

        var scoped=context.User.FindAll("scope")
            .SelectMany(x=>x.Value.Split(' ',StringSplitOptions.RemoveEmptyEntries))
            .Contains(requirement.Operation,StringComparer.Ordinal);
        if (!scoped) return;

        try
        {
            if(await store.IsAllowedAsync(merchant,issuer,subject,clientId,
                requirement.Operation,CancellationToken.None))
                context.Succeed(requirement);
        }
        catch (Exception ex) when(ex is NpgsqlException or TimeoutException)
        {
            // Authorization never succeeds when registry unavailable.
            logger.LogError(ex,"Merchant registry unavailable; denying scoped access");
        }
    }
}

public sealed class MerchantSecurityAuditMiddleware(
    RequestDelegate next, NpgsqlDataSource source,
    ILogger<MerchantSecurityAuditMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        // This captures all merchant route responses, including 401/403, but
        // is not an immutable financial audit and is not part of the Payment transaction.
        var path=context.Request.Path.Value ?? "";
        if (!path.StartsWith("/v1/payments",StringComparison.OrdinalIgnoreCase)
            && !path.StartsWith("/v1/refunds",StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        try { await next(context); }
        finally
        {
            try
            {
                var principal=context.User;
                var merchant=MerchantJwtAuthentication.TryGetMerchantId(principal,out var id)
                    ? (Guid?)id : null;
                await using var conn=await source.OpenConnectionAsync(CancellationToken.None);
                await using var cmd=new NpgsqlCommand("""
                    INSERT INTO iam.access_audit
                        (audit_id,request_id,merchant_id,subject,client_id,
                         method,request_path,http_status)
                    VALUES (@audit,@request,@merchant,@subject,@client,
                            @method,@path,@status)
                    """,conn);
                cmd.Parameters.AddWithValue("audit",Guid.NewGuid());
                cmd.Parameters.AddWithValue("request",context.TraceIdentifier[..Math.Min(128,context.TraceIdentifier.Length)]);
                cmd.Parameters.AddWithValue("merchant",(object?)merchant ?? DBNull.Value);
                cmd.Parameters.AddWithValue("subject",(object?)principal.FindFirst("sub")?.Value ?? DBNull.Value);
                cmd.Parameters.AddWithValue("client",(object?)principal.FindFirst("client_id")?.Value ?? DBNull.Value);
                cmd.Parameters.AddWithValue("method",context.Request.Method);
                cmd.Parameters.AddWithValue("path",path[..Math.Min(512,path.Length)]);
                cmd.Parameters.AddWithValue("status",context.Response.StatusCode);
                await cmd.ExecuteNonQueryAsync(CancellationToken.None);
            }
            catch(Exception ex)
            {
                logger.LogError(ex,"Best-effort security access audit failed; financial audit gate remains open");
            }
        }
    }
}
