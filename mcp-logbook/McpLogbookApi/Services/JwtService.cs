using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using McpLogbookApi.Models;
using Microsoft.IdentityModel.Tokens;

namespace McpLogbookApi.Services;

public class JwtService
{
    private readonly IConfiguration _config;

    // In-memory store: refreshToken → UserInfo
    // In production this would be a database
    private readonly Dictionary<string, (UserInfo User, DateTime Expiry)> _refreshTokens = new();

    public JwtService(IConfiguration config)
    {
        _config = config;
    }

    public AuthResponse GenerateTokens(UserInfo user)
    {
        var jwtSettings = _config.GetSection("Jwt");
        var accessExpiry = DateTime.UtcNow.AddHours(double.Parse(jwtSettings["ExpiryHours"]!));
        var refreshExpiry = DateTime.UtcNow.AddDays(7);

        var accessToken = GenerateAccessToken(user, accessExpiry, jwtSettings);
        var refreshToken = GenerateRefreshToken();

        // Store refresh token
        _refreshTokens[refreshToken] = (user, refreshExpiry);

        return new AuthResponse
        {
            Message = "Login successful",
            Username = user.Username,
            Role = user.Role,
            TenantId = user.TenantId,
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            AccessTokenExpiry = accessExpiry,
            RefreshTokenExpiry = refreshExpiry
        };
    }

    public AuthResponse? RefreshTokens(string refreshToken)
    {
        if (!_refreshTokens.TryGetValue(refreshToken, out var entry))
            return null;

        if (entry.Expiry < DateTime.UtcNow)
        {
            _refreshTokens.Remove(refreshToken);
            return null;
        }

        // Rotate refresh token — old one is invalidated
        _refreshTokens.Remove(refreshToken);
        return GenerateTokens(entry.User);
    }

    public bool RevokeRefreshToken(string refreshToken)
    {
        return _refreshTokens.Remove(refreshToken);
    }

    private string GenerateAccessToken(UserInfo user, DateTime expiry, IConfigurationSection jwtSettings)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.Name,   user.Username),
            new Claim(ClaimTypes.Role,   user.Role),
            new Claim("TenantId",        user.TenantId)
        };

        var key   = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings["SecretKey"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer:             jwtSettings["Issuer"],
            audience:           jwtSettings["Audience"],
            claims:             claims,
            expires:            expiry,
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string GenerateRefreshToken()
    {
        var bytes = new byte[64];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }
}