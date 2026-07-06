using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace McpLogbookApi.Tests;

// Boots the real app but replaces Entra ID validation with a local test key,
// and points the SQLite connection string at a fresh temp-file DB per run
public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string TestKey      = "TestSecretKeyForAuthorizationTests2024!XYZ";
    public const string TestIssuer   = "TestIssuer";
    public const string TestAudience = "TestAudience";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            // Isolated, freshly-seeded database per test factory instance
            var testDbPath = Path.Combine(Path.GetTempPath(), $"test-logbook-{Guid.NewGuid()}.db");
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:LogbookDb"] = $"Data Source={testDbPath}"
            });
        });

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

    // ── Ship-scoped logbook data ───────────────────────────────────
    // Seed data: Alice Mercer -> MV OCEAN STAR + MV NORTHERN LIGHT
    //            Rahul Verma  -> MV NORTHERN LIGHT only
    //            Sofia Nunez  -> MV PACIFIC DAWN only

    [Fact]
    public async Task Logbooks_ReturnsOnlyAssignedShipsData()
    {
        SetToken(CustomWebApplicationFactory.CreateToken("sofia.nunez@example.com", "ReadOnlyUser", "n/a"));
        var response = await _client.GetAsync("/api/mcp/logbooks");
        var body     = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("MV PACIFIC DAWN", body);
        Assert.DoesNotContain("MV OCEAN STAR", body);
        Assert.DoesNotContain("MV NORTHERN LIGHT", body);
    }

    // A user assigned to multiple ships sees all of them, and nothing else
    [Fact]
    public async Task Logbooks_UserWithMultipleShips_SeesBothShipsData()
    {
        SetToken(CustomWebApplicationFactory.CreateToken("alice.mercer@example.com", "Administrator", "n/a"));
        var response = await _client.GetAsync("/api/mcp/logbooks");
        var body     = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("MV OCEAN STAR", body);
        Assert.Contains("MV NORTHERN LIGHT", body);
        Assert.DoesNotContain("MV PACIFIC DAWN", body);
    }

    // A user with no ship assignment at all gets 404, same as an empty result set
    [Fact]
    public async Task Logbooks_UnassignedUser_Returns404()
    {
        SetToken(CustomWebApplicationFactory.CreateToken("nobody@example.com", "Administrator", "n/a"));
        var response = await _client.GetAsync("/api/mcp/logbooks");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Single logbook entry ───────────────────────────────────────

    // Log 1 belongs to MV OCEAN STAR, one of Alice's assigned ships
    [Fact]
    public async Task LogbookById_AuthorizedShip_ReturnsEntry()
    {
        SetToken(CustomWebApplicationFactory.CreateToken("alice.mercer@example.com", "Administrator", "n/a"));
        var response = await _client.GetAsync("/api/mcp/logbooks/1");
        var body     = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("MV OCEAN STAR", body);
    }

    // Requirement: a caller must never be able to tell "no such entry" apart from
    // "it exists, but you don't have access" — both must produce identical responses.
    // Log 1 belongs to MV OCEAN STAR, which Rahul is NOT assigned to.
    [Fact]
    public async Task LogbookById_UnauthorizedShip_And_NonexistentId_ReturnIdenticalResponses()
    {
        SetToken(CustomWebApplicationFactory.CreateToken("rahul.verma@example.com", "Superintendent", "n/a"));

        var unauthorizedResponse = await _client.GetAsync("/api/mcp/logbooks/1");
        var unauthorizedBody     = await unauthorizedResponse.Content.ReadAsStringAsync();

        var nonexistentResponse = await _client.GetAsync("/api/mcp/logbooks/9999");
        var nonexistentBody     = await nonexistentResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, unauthorizedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, nonexistentResponse.StatusCode);
        Assert.Equal(unauthorizedBody, nonexistentBody);
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
