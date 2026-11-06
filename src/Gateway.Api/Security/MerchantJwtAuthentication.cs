using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

namespace PaymentGateway.Gateway.Api.Security;

/// <summary>
/// MOCK-only gateway. Development HS256 for locally minted tokens, or an
/// opt-in external OIDC/JWKS validation candidate (never connects to real PSPs).
/// The whole application still blocks non-Development startup.
/// </summary>
public static class MerchantJwtAuthentication
{
    public const string DevIssuer="https://merchant-auth.local.test";
    public const string Audience="paymentgateway.mock.api";
    public const string MerchantClaim="merchant_id";

    public static IServiceCollection AddMerchantAuthentication(
        this IServiceCollection services,IConfiguration cfg,IWebHostEnvironment environment)
    {
        if(!environment.IsDevelopment())
            throw new InvalidOperationException("MOCK-only Gateway refuses non-Development deployment.");

        var mode=cfg["MerchantAuth:Mode"] ?? "DevelopmentHmac";
        string? key=null;
        string? authority=null, oidcIssuer=null, oidcAudience=null;
        if(mode=="DevelopmentHmac")
        {
            key=cfg["MerchantAuth:DevSigningKey"];
            if(string.IsNullOrWhiteSpace(key) || Encoding.UTF8.GetByteCount(key)<32)
                throw new InvalidOperationException("Missing 32+ byte local Development signing key.");
        }
        else if(mode=="Oidc")
        {
            authority=cfg["MerchantAuth:Authority"];
            oidcIssuer=cfg["MerchantAuth:Issuer"];
            oidcAudience=cfg["MerchantAuth:Audience"];
            if(!Uri.TryCreate(authority,UriKind.Absolute,out var uri)
                || uri.Scheme!="https" || string.IsNullOrWhiteSpace(uri.Host)
                || !string.IsNullOrWhiteSpace(uri.UserInfo)
                || !string.IsNullOrWhiteSpace(uri.Fragment)
                || string.IsNullOrWhiteSpace(oidcIssuer)
                || !Uri.TryCreate(oidcIssuer,UriKind.Absolute,out var issuerUri)
                || issuerUri.Scheme!="https"
                || string.IsNullOrWhiteSpace(oidcAudience))
                throw new InvalidOperationException("Oidc mode requires HTTPS Authority, Issuer and Audience.");
        }
        else throw new InvalidOperationException("Unknown MerchantAuth:Mode; fail closed.");

        services.AddSingleton<MerchantAccessStore>();
        services.AddSingleton<IAuthorizationHandler,MerchantOperationHandler>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options=>{
                options.MapInboundClaims=false;
                options.RequireHttpsMetadata=true;
                if(mode=="DevelopmentHmac")
                {
                    options.TokenValidationParameters=new TokenValidationParameters{
                        ValidateIssuer=true,ValidIssuer=DevIssuer,
                        ValidateAudience=true,ValidAudience=Audience,
                        ValidateIssuerSigningKey=true,
                        IssuerSigningKey=new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key!)),
                        RequireSignedTokens=true,ValidateLifetime=true,RequireExpirationTime=true,
                        ValidAlgorithms=[SecurityAlgorithms.HmacSha256],ClockSkew=TimeSpan.Zero
                    };
                }
                else
                {
                    // Standard JwtBearer metadata/JWKS discovery. Provider-specific
                    // claims, key rotation, test issuer and trust policy remain unverified.
                    options.Authority=authority;
                    options.Audience=oidcAudience;
                    options.TokenValidationParameters=new TokenValidationParameters{
                        ValidateIssuer=true,ValidIssuer=oidcIssuer,
                        ValidateAudience=true,ValidAudience=oidcAudience,
                        ValidateIssuerSigningKey=true,
                        RequireSignedTokens=true,ValidateLifetime=true,RequireExpirationTime=true,
                        ValidAlgorithms=[SecurityAlgorithms.RsaSha256],
                        ClockSkew=TimeSpan.FromSeconds(30)
                    };
                }
            });

        services.AddAuthorization(options=>{
            // Unclassified future endpoints deny by default: a developer must explicitly
            // apply an operation policy, or mark public/provider endpoint intentionally.
            options.FallbackPolicy=new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new MerchantOperationRequirement("__deny_unclassified__"))
                .Build();
            foreach(var op in new[]{"payments:create","payments:read","refunds:create","refunds:read"})
                options.AddPolicy(op,policy=>policy.RequireAuthenticatedUser()
                    .AddRequirements(new MerchantOperationRequirement(op)));
        });
        return services;
    }

    public static bool TryGetMerchantId(ClaimsPrincipal principal,out Guid merchantId)
    {
        merchantId=Guid.Empty;
        if(principal.Identity?.IsAuthenticated!=true
            || string.IsNullOrWhiteSpace(principal.FindFirst("sub")?.Value))
            return false;
        var matches=principal.FindAll(MerchantClaim).ToArray();
        return matches.Length==1 &&
            Guid.TryParseExact(matches[0].Value,"D",out merchantId) &&
            merchantId!=Guid.Empty;
    }
}
