using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace McpLogbookApi.Tests;

// Boots the real app but replaces Entra ID validation with a local test key
public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string TestKey      = "TestSecretKeyForAuthorizationTests2024!XYZ";
    public const string TestIssuer   = "TestIssuer";
    public const string TestAudience = "TestAudience";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.PostConfigureAll<JwtBearerOptions>(options =>
            {
                options.Authority             = null;
                options.RequireHttpsMetadata  = false;
                options.Audience              = TestAudience;
                // Clears the OIDC ConfigurationManager so no Entra ID network call is attempted
                options.ConfigurationManager  = null;
                // Disables inbound claim mapping so "tid", "roles", "preferred_username"
                // pass through unchanged — without this, "tid" gets remapped to a long URI
                // and User.FindFirstValue("tid") returns null
                options.MapInboundClaims      = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer    = true,
                    ValidIssuer       = TestIssuer,
                    ValidateAudience  = true,
                    ValidAudience     = TestAudience,
                    ValidateLifetime  = true,
                    IssuerSigningKey  = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestKey)),
                    // Mirror production Entra ID claim names exactly
                    NameClaimType     = "preferred_username",
                    RoleClaimType     = "roles"
                };
            });
        });
    }

    // Builds a signed JWT using the same claim names Entra ID would issue
    public static string CreateToken(string username, string role, string tenantId)
    {
        var claims = new[]
        {
            new Claim("preferred_username", username),
            new Claim("roles",              role),
            new Claim("tid",                tenantId)
        };

        var key   = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer:             TestIssuer,
            audience:           TestAudience,
            claims:             claims,
            expires:            DateTime.UtcNow.AddHours(1),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

public class AuthorizationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public AuthorizationTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    private void SetToken(string token) =>
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private void ClearToken() =>
        _client.DefaultRequestHeaders.Authorization = null;

    // ── Unauthenticated ────────────────────────────────────────────

    // No token at all should return 401
    [Fact]
    public async Task NoToken_Returns401()
    {
        ClearToken();
        var response = await _client.GetAsync("/api/mcp/readonly");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // Garbage string in Authorization header should return 401
    [Fact]
    public async Task InvalidToken_Returns401()
    {
        SetToken("this-is-not-a-valid-token");
        var response = await _client.GetAsync("/api/mcp/readonly");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── Admin endpoint ─────────────────────────────────────────────

    // Administrator should get 200 on /api/mcp/admin
    [Fact]
    public async Task Administrator_CanAccess_AdminEndpoint()
    {
        SetToken(CustomWebApplicationFactory.CreateToken("alice@company.com", "Administrator", "nordic-shipping"));
        var response = await _client.GetAsync("/api/mcp/admin");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // Superintendent does not have AdminOnly policy → 403
    [Fact]
    public async Task Superintendent_CannotAccess_AdminEndpoint()
    {
        SetToken(CustomWebApplicationFactory.CreateToken("bob@company.com", "Superintendent", "nordic-shipping"));
        var response = await _client.GetAsync("/api/mcp/admin");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // VesselUser does not have AdminOnly policy → 403
    [Fact]
    public async Task VesselUser_CannotAccess_AdminEndpoint()
    {
        SetToken(CustomWebApplicationFactory.CreateToken("charlie@company.com", "VesselUser", "pacific-maritime"));
        var response = await _client.GetAsync("/api/mcp/admin");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ReadOnlyUser does not have AdminOnly policy → 403
    [Fact]
    public async Task ReadOnlyUser_CannotAccess_AdminEndpoint()
    {
        SetToken(CustomWebApplicationFactory.CreateToken("diana@company.com", "ReadOnlyUser", "atlantic-fleet"));
        var response = await _client.GetAsync("/api/mcp/admin");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── Superintendent endpoint ────────────────────────────────────

    // Superintendent satisfies SuperintendentUp policy → 200
    [Fact]
    public async Task Superintendent_CanAccess_SuperintendentEndpoint()
    {
        SetToken(CustomWebApplicationFactory.CreateToken("bob@company.com", "Superintendent", "nordic-shipping"));
        var response = await _client.GetAsync("/api/mcp/superintendent");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // VesselUser is below Superintendent → 403
    [Fact]
    public async Task VesselUser_CannotAccess_SuperintendentEndpoint()
    {
        SetToken(CustomWebApplicationFactory.CreateToken("charlie@company.com", "VesselUser", "pacific-maritime"));
        var response = await _client.GetAsync("/api/mcp/superintendent");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── Vessel endpoint ────────────────────────────────────────────

    // VesselUser satisfies VesselUserUp policy → 200
    [Fact]
    public async Task VesselUser_CanAccess_VesselEndpoint()
    {
        SetToken(CustomWebApplicationFactory.CreateToken("charlie@company.com", "VesselUser", "pacific-maritime"));
        var response = await _client.GetAsync("/api/mcp/vessel");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ReadOnlyUser is below VesselUser → 403
    [Fact]
    public async Task ReadOnlyUser_CannotAccess_VesselEndpoint()
    {
        SetToken(CustomWebApplicationFactory.CreateToken("diana@company.com", "ReadOnlyUser", "atlantic-fleet"));
        var response = await _client.GetAsync("/api/mcp/vessel");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── Readonly endpoint ──────────────────────────────────────────

    // ReadOnlyUser is the lowest role — should still get 200 on readonly
    [Fact]
    public async Task ReadOnlyUser_CanAccess_ReadonlyEndpoint()
    {
        SetToken(CustomWebApplicationFactory.CreateToken("diana@company.com", "ReadOnlyUser", "atlantic-fleet"));
        var response = await _client.GetAsync("/api/mcp/readonly");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // Administrator also satisfies ReadOnlyUp (includes all roles)
    [Fact]
    public async Task Administrator_CanAccess_ReadonlyEndpoint()
    {
        SetToken(CustomWebApplicationFactory.CreateToken("alice@company.com", "Administrator", "nordic-shipping"));
        var response = await _client.GetAsync("/api/mcp/readonly");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── Tenant isolation ───────────────────────────────────────────

    // nordic-shipping user should only see nordic-shipping logbooks
    [Fact]
    public async Task Logbooks_ReturnsOnlyOwnTenantData()
    {
        SetToken(CustomWebApplicationFactory.CreateToken("alice@company.com", "Administrator", "nordic-shipping"));
        var response = await _client.GetAsync("/api/mcp/logbooks");
        var body     = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("nordic-shipping", body);
        Assert.DoesNotContain("pacific-maritime", body);
        Assert.DoesNotContain("atlantic-fleet", body);
    }

    // pacific-maritime user should not see nordic-shipping data
    [Fact]
    public async Task Logbooks_DifferentTenants_DoNotShareData()
    {
        SetToken(CustomWebApplicationFactory.CreateToken("charlie@company.com", "VesselUser", "pacific-maritime"));
        var response = await _client.GetAsync("/api/mcp/logbooks");
        var body     = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("pacific-maritime", body);
        Assert.DoesNotContain("nordic-shipping", body);
    }

    // Tenant with no mock logbooks should get 404
    [Fact]
    public async Task Logbooks_UnknownTenant_Returns404()
    {
        SetToken(CustomWebApplicationFactory.CreateToken("eve@company.com", "Administrator", "unknown-tenant"));
        var response = await _client.GetAsync("/api/mcp/logbooks");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Audit endpoints ────────────────────────────────────────────

    // Administrator can view all audit logs
    [Fact]
    public async Task Administrator_CanAccess_AllAuditLogs()
    {
        SetToken(CustomWebApplicationFactory.CreateToken("alice@company.com", "Administrator", "nordic-shipping"));
        var response = await _client.GetAsync("/api/audit/all");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // Superintendent can view their own tenant audit logs
    [Fact]
    public async Task Superintendent_CanAccess_TenantAuditLogs()
    {
        SetToken(CustomWebApplicationFactory.CreateToken("bob@company.com", "Superintendent", "nordic-shipping"));
        var response = await _client.GetAsync("/api/audit/my-tenant");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // VesselUser cannot access audit logs at all
    [Fact]
    public async Task VesselUser_CannotAccess_AuditLogs()
    {
        SetToken(CustomWebApplicationFactory.CreateToken("charlie@company.com", "VesselUser", "pacific-maritime"));
        var response = await _client.GetAsync("/api/audit/my-tenant");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // Superintendent cannot access the all-tenants audit log
    [Fact]
    public async Task Superintendent_CannotAccess_AllAuditLogs()
    {
        SetToken(CustomWebApplicationFactory.CreateToken("bob@company.com", "Superintendent", "nordic-shipping"));
        var response = await _client.GetAsync("/api/audit/all");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
