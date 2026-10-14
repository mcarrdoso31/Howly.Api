using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BZ.Howly.Api.Core.Identity;
using BZ.Howly.Api.Core.Interfaces.Service;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace BZ.Howly.Api.Utils.Security;

public sealed class JwtTokenService : ITokenService
{
    private const int MinimumKeyBytes = 32;
    private const int RefreshTokenBytes = 64;

    private readonly JwtOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly SigningCredentials _signingCredentials;
    private readonly JsonWebTokenHandler _handler = new();

    public JwtTokenService(IOptions<JwtOptions> options, TimeProvider timeProvider)
    {
        _options = options.Value;
        _timeProvider = timeProvider;

        var keyBytes = Encoding.UTF8.GetBytes(_options.SigningKey);
        if (keyBytes.Length < MinimumKeyBytes)
            throw new ArgumentException($"Jwt:SigningKey must be at least {MinimumKeyBytes} bytes.", nameof(options));

        _signingCredentials = new SigningCredentials(new SymmetricSecurityKey(keyBytes), SecurityAlgorithms.HmacSha256);
    }

    public AccessToken CreateAccessToken(UserAccount user)
    {
        var now = _timeProvider.GetUtcNow();
        var expiresAt = now.Add(_options.AccessTokenLifetime);

        var claims = new List<Claim>
        {
            new(JwtClaimTypes.Subject, user.Id.ToString()),
            new(JwtClaimTypes.Name, user.DisplayName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };
        claims.AddRange(user.Roles.Select(role => new Claim(JwtClaimTypes.Role, role)));
        if (user.MustChangePassword)
            claims.Add(new Claim(JwtClaimTypes.PasswordChangeRequired, "true"));
        if (user.MustConfirmProfile)
            claims.Add(new Claim(JwtClaimTypes.ProfileConfirmationRequired, "true"));

        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = _signingCredentials,
        });

        return new AccessToken(token, expiresAt);
    }

    public IssuedRefreshToken CreateRefreshToken()
    {
        var value = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(RefreshTokenBytes));
        return new IssuedRefreshToken(value, HashRefreshToken(value), _timeProvider.GetUtcNow().Add(_options.RefreshTokenLifetime));
    }

    public string HashRefreshToken(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
