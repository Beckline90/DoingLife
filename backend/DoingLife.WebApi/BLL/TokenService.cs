using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using DoingLife.WebApi.BLL.Interfaces;
using DoingLife.WebApi.Entities;
using DoingLife.WebApi.Shared.Helper;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace DoingLife.WebApi.BLL;

public class TokenService : ITokenService
{
    private readonly JwtSettings _settings;
    private readonly SymmetricSecurityKey _signingKey;

    public TokenService(IOptions<JwtSettings> options)
    {
        _settings = options.Value;
        _signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Key));
    }

    public (string Token, DateTime ExpiresAt) CreateAccessToken(User user, IEnumerable<string> roles)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var now = DateTime.UtcNow;
        var expiresAt = now.AddMinutes(_settings.AccessTokenExpirationMinutes);

        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            notBefore: now,
            expires: expiresAt,
            signingCredentials: new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256));

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    public string CreateRefreshToken()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
    }

    public string HashRefreshToken(string token)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hash);
    }
}