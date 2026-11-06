using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace PaymentGateway.Gateway.Api.Security;

/// <summary>
/// Explicit Development-only JWT validation for the local MOCK case study.
/// No token-mint endpoint, no unsigned JWTs, no X-Demo-Merchant-Id trust.
/// A real OIDC issuer, asymmetric JWKS validation, tenant provisioning,
/// key rotation and production access policies need separate approval.
/// </summary>
public static class MerchantJwtAuthentication
{
    public const string DevIssuer = "https://merchant-auth.local.test";
    public const string Audience = "paymentgateway.mock.api";
    public const string MerchantClaim = "merchant_id";

    public static IServiceCollection AddMerchantAuthentication(
        this IServiceCollection services, IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        // Preserve the existing strict non-Development startup gate because this
        // repository exposes a MOCK bank/payment processor, not a production PSP.
        if (!environment.IsDevelopment())
            throw new InvalidOperationException(
                "This MOCK-only Gateway is not approved outside Development.");

        var key = configuration["MerchantAuth:DevSigningKey"];
        if (string.IsNullOrEmpty(key) || Encoding.UTF8.GetByteCount(key) < 32)
            throw new InvalidOperationException(
                "Set MerchantAuth:DevSigningKey (32+ UTF-8 bytes) for local Development only.");

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false; // Claims have exact wire names.
                options.RequireHttpsMetadata = true;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = DevIssuer,
                    ValidateAudience = true,
                    ValidAudience = Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    RequireSignedTokens = true,
                    ClockSkew = TimeSpan.Zero,
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256]
                };
            });

        services.AddAuthorization(options =>
        {
            // By default, new HTTP endpoints should never be accidentally public.
            options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser().Build();

            foreach (var operation in new[]
            {
                "payments:create", "payments:read", "refunds:create", "refunds:read"
            })
            {
                options.AddPolicy(operation, policy =>
                    policy.RequireAuthenticatedUser()
                        .RequireAssertion(context =>
                            TryGetMerchantId(context.User, out _) &&
                            HasScope(context.User, operation)));
            }
        });
        return services;
    }

    public static bool TryGetMerchantId(ClaimsPrincipal principal, out Guid merchantId)
    {
        merchantId = Guid.Empty;
        if (principal.Identity?.IsAuthenticated != true ||
            string.IsNullOrWhiteSpace(principal.FindFirst("sub")?.Value))
            return false;
        var values = principal.FindAll(MerchantClaim).ToArray();
        return values.Length == 1 &&
            Guid.TryParseExact(values[0].Value, "D", out merchantId) &&
            merchantId != Guid.Empty;
    }

    private static bool HasScope(ClaimsPrincipal principal, string required) =>
        principal.FindAll("scope").Any(c =>
            c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Contains(required, StringComparer.Ordinal));
}
