using System.Security.Cryptography;
using Gamex.Messaging.Application.Abstractions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Gamex.Messaging.Infrastructure.Security;

/// <summary>Public key and expected claims of tokens issued by the User Library Service.</summary>
public sealed class JwtValidationOptions
{
    public const string ContactAudience = "gamex-listing-contact";
    public const string ContactTokenType = "gx-contact+jwt";

    /// <summary>Null only in Development without <c>JWT_PUBLIC_KEY</c>: every token is then rejected.</summary>
    public RsaSecurityKey? Key { get; init; }

    /// <summary><c>JWT_ISSUER</c>; validated when set.</summary>
    public string? Issuer { get; init; }

    /// <summary><c>JWT_AUDIENCE</c> of access tokens; validated when set.</summary>
    public string? Audience { get; init; }

    public static RsaSecurityKey? LoadKey(string? pem)
    {
        if (string.IsNullOrWhiteSpace(pem))
        {
            return null;
        }

        // Environment variables often carry the PEM with escaped newlines.
        var rsa = RSA.Create();
        rsa.ImportFromPem(pem.Replace("\\n", "\n", StringComparison.Ordinal));
        return new RsaSecurityKey(rsa);
    }
}

/// <summary>
/// Verifies listing contact tokens (SPEC-user-library §11.4): signed by the User Library Service, audience
/// <c>gamex-listing-contact</c>, type <c>gx-contact+jwt</c>. Access tokens are rejected by the audience and type checks.
/// </summary>
internal sealed class ContactTokenVerifier(JwtValidationOptions options) : IContactTokenVerifier
{
    private readonly JsonWebTokenHandler _handler = new();

    public async Task<ListingContact?> VerifyAsync(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || options.Key is null)
        {
            return null;
        }

        var result = await _handler.ValidateTokenAsync(token, new TokenValidationParameters
        {
            IssuerSigningKey = options.Key,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            ValidateIssuer = !string.IsNullOrEmpty(options.Issuer),
            ValidIssuer = options.Issuer,
            ValidAudience = JwtValidationOptions.ContactAudience,
            ValidTypes = [JwtValidationOptions.ContactTokenType],
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        });

        if (!result.IsValid
            || !TryGuid(result.Claims, "listing_id", out var listingId)
            || !TryGuid(result.Claims, "owner_id", out var ownerId)
            || Text(result.Claims, "owner_name") is not string { Length: > 0 } ownerName
            || Text(result.Claims, "game_title") is not string { Length: > 0 } gameTitle)
        {
            return null;
        }

        return new ListingContact(listingId, ownerId, ownerName, Text(result.Claims, "owner_avatar"), gameTitle);
    }

    private static string? Text(IDictionary<string, object> claims, string name) =>
        claims.TryGetValue(name, out var raw) ? raw as string : null;

    private static bool TryGuid(IDictionary<string, object> claims, string name, out Guid value)
    {
        value = default;
        return claims.TryGetValue(name, out var raw) && Guid.TryParse(raw as string, out value);
    }
}
