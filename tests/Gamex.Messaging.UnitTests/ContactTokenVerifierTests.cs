using System.Security.Cryptography;
using Gamex.Messaging.Infrastructure.Security;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Gamex.Messaging.UnitTests;

public sealed class ContactTokenVerifierTests
{
    private const string Issuer = "gamex-user-library";
    private static readonly RsaSecurityKey Key = new(RSA.Create(2048));
    private static readonly Guid ListingId = Guid.NewGuid();
    private static readonly Guid OwnerId = Guid.NewGuid();

    private static ContactTokenVerifier Verifier() => new(new JwtValidationOptions { Key = Key, Issuer = Issuer });

    private static string Token(
        RsaSecurityKey? key = null,
        string audience = JwtValidationOptions.ContactAudience,
        string? type = JwtValidationOptions.ContactTokenType,
        DateTime? expires = null,
        bool withTitle = true)
    {
        var now = DateTime.UtcNow;
        var claims = new Dictionary<string, object>
        {
            ["listing_id"] = ListingId.ToString(),
            ["owner_id"] = OwnerId.ToString(),
            ["owner_name"] = "Ana",
        };
        if (withTitle)
        {
            claims["game_title"] = "Hades";
        }

        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(new SecurityTokenDescriptor
        {
            TokenType = type,
            Issuer = Issuer,
            Audience = audience,
            IssuedAt = (expires ?? now.AddHours(1)).AddHours(-2),
            NotBefore = (expires ?? now.AddHours(1)).AddHours(-2),
            Expires = expires ?? now.AddHours(1),
            Claims = claims,
            SigningCredentials = new SigningCredentials(key ?? Key, SecurityAlgorithms.RsaSha256),
        });
    }

    [Fact]
    public async Task Valid_ReturnsContact()
    {
        var contact = await Verifier().VerifyAsync(Token());

        contact.ShouldNotBeNull();
        contact.ListingId.ShouldBe(ListingId);
        contact.OwnerId.ShouldBe(OwnerId);
        contact.OwnerName.ShouldBe("Ana");
        contact.GameTitle.ShouldBe("Hades");
        contact.OwnerAvatarUrl.ShouldBeNull();
    }

    [Fact]
    public async Task AccessToken_IsRejected()
    {
        (await Verifier().VerifyAsync(Token(audience: "gamex", type: "JWT"))).ShouldBeNull();
    }

    [Fact]
    public async Task OtherSigner_IsRejected()
    {
        (await Verifier().VerifyAsync(Token(key: new RsaSecurityKey(RSA.Create(2048))))).ShouldBeNull();
    }

    [Fact]
    public async Task Expired_IsRejected()
    {
        (await Verifier().VerifyAsync(Token(expires: DateTime.UtcNow.AddMinutes(-5)))).ShouldBeNull();
    }

    [Fact]
    public async Task MissingClaim_IsRejected()
    {
        (await Verifier().VerifyAsync(Token(withTitle: false))).ShouldBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not.a.jwt")]
    public async Task Garbage_IsRejected(string? token)
    {
        (await Verifier().VerifyAsync(token)).ShouldBeNull();
    }

    [Fact]
    public async Task NoKeyConfigured_RejectsEverything()
    {
        (await new ContactTokenVerifier(new JwtValidationOptions()).VerifyAsync(Token())).ShouldBeNull();
    }
}
