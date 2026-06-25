using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using McpLogbookApi.Models;
using Microsoft.IdentityModel.Tokens;

namespace McpLogbookApi.Services;

public class JwtService
{
    private readonly IConfiguration _config;

    public JwtService(IConfiguration config)
    {
        _config = config;
    }

    public string GenerateToken(UserInfo user)
    {
        var jwtSettings = _config.GetSection("Jwt");

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
            expires:            DateTime.UtcNow.AddHours(double.Parse(jwtSettings["ExpiryHours"]!)),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}