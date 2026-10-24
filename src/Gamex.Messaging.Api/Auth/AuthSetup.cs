using System.Security.Claims;
using System.Threading.RateLimiting;
using Gamex.Messaging.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Gamex.Messaging.Api.Auth;

internal static class AuthSetup
{
    public const string SendRateLimit = "send";
    public const string HubPath = "/hubs/messaging";

    public static IServiceCollection AddGamexAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<JwtValidationOptions>((options, jwt) =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    // Without a key (Development only) every token fails validation.
                    IssuerSigningKey = jwt.Key,
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                    ValidateIssuer = !string.IsNullOrEmpty(jwt.Issuer),
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = !string.IsNullOrEmpty(jwt.Audience),
                    ValidAudience = jwt.Audience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    RoleClaimType = "role",
                    NameClaimType = "name",
                };

                // WebSockets cannot send headers: SignalR passes the JWT as ?access_token= (SPEC-messaging §4).
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var token = context.Request.Query["access_token"];
                        if (!string.IsNullOrEmpty(token) && context.HttpContext.Request.Path.StartsWithSegments(HubPath))
                        {
                            context.Token = token;
                        }

                        return Task.CompletedTask;
                    },
                };
            });

        services.AddAuthorization();

        // Spam protection: messages per user per minute.
        var perMinute = configuration.GetValue("SEND_RATE_LIMIT_PER_MINUTE", 30);
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(SendRateLimit, context => RateLimitPartition.GetFixedWindowLimiter(
                context.User.FindFirstValue("sub") ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });

        return services;
    }

    public static Guid UserId(this ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue("sub") ?? throw new InvalidOperationException("Missing sub claim."));

    /// <summary>Display name from the JWT <c>name</c> claim (falls back to "Gamex user").</summary>
    public static string DisplayName(this ClaimsPrincipal user) =>
        user.FindFirstValue("name") is { Length: > 0 } name ? (name.Length > 100 ? name[..100] : name) : "Gamex user";
}
