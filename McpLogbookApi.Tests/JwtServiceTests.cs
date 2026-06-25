using System.IdentityModel.Tokens.Jwt;
using McpLogbookApi.Models;
using McpLogbookApi.Services;
using Microsoft.Extensions.Configuration;

namespace McpLogbookApi.Tests;

public class JwtServiceTests
{
    private readonly JwtService _jwtService;

    public JwtServiceTests()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:SecretKey"]   = "NavtorMcpSuperSecretKey2024!@#$XYZ",
                ["Jwt:Issuer"]      = "McpLogbookApi",
                ["Jwt:Audience"]    = "McpLogbookClient",
                ["Jwt:ExpiryHours"] = "2"
            })
            .Build();

        _jwtService = new JwtService(config);
    }

    [Fact]
    public void Login_ReturnsAccessTokenAndRefreshToken()
    {
        var user = new UserInfo { Username = "alice", Role = "Administrator", TenantId = "nordic-shipping" };

        var result = _jwtService.GenerateTokens(user);

        Assert.NotNull(result.AccessToken);
        Assert.NotNull(result.RefreshToken);
        Assert.Equal("alice", result.Username);
        Assert.Equal("Administrator", result.Role);
        Assert.Equal("nordic-shipping", result.TenantId);
    }

    [Fact]
    public void AccessToken_ContainsCorrectClaims()
    {
        var user = new UserInfo { Username = "bob", Role = "Superintendent", TenantId = "pacific-maritime" };

        var result = _jwtService.GenerateTokens(user);

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(result.AccessToken);

        Assert.Contains(jwt.Claims, c => c.Value == "bob");
        Assert.Contains(jwt.Claims, c => c.Value == "Superintendent");
        Assert.Contains(jwt.Claims, c => c.Value == "pacific-maritime");
    }
    [Fact]

    public void RefreshToken_ReturnsNewTokens()
    {
        var user = new UserInfo { Username = "charlie", Role = "VesselUser", TenantId = "atlantic-fleet" };

        var initial = _jwtService.GenerateTokens(user);

        Thread.Sleep(1000); // ensure different token timestamp

        var refreshed = _jwtService.RefreshTokens(initial.RefreshToken);

        Assert.NotNull(refreshed);
        Assert.NotEqual(initial.RefreshToken, refreshed!.RefreshToken); // refresh token must rotate
    }

    [Fact]
    public void RefreshToken_FailsAfterRevocation()
    {
        var user = new UserInfo { Username = "diana", Role = "ReadOnlyUser", TenantId = "nordic-shipping" };

        var result = _jwtService.GenerateTokens(user);
        _jwtService.RevokeRefreshToken(result.RefreshToken);

        var refreshed = _jwtService.RefreshTokens(result.RefreshToken);

        Assert.Null(refreshed);
    }

    [Fact]
    public void RefreshToken_FailsWithInvalidToken()
    {
        var refreshed = _jwtService.RefreshTokens("invalid-token-xyz");

        Assert.Null(refreshed);
    }
}