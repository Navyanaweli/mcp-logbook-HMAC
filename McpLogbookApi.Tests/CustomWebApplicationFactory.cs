using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace McpLogbookApi.Tests;

// Boots the real app, points the SQLite connection string at a fresh temp-file DB
// per run, and sets a known Admin:HmacSecret so tests can sign requests to the
// admin-only endpoints (see AdminHmacAuthenticationMiddleware).
public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    // Any valid base64 string works as an HMAC key -- doesn't need to be exactly 32 bytes.
    public const string TestAdminHmacSecret = "dGVzdC1hZG1pbi1obWFjLXNlY3JldC1mb3ItdGVzdHM=";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            // Isolated, freshly-seeded database per test factory instance
            var testDbPath = Path.Combine(Path.GetTempPath(), $"test-logbook-{Guid.NewGuid()}.db");
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:LogbookDb"] = $"Data Source={testDbPath}",
                ["Admin:HmacSecret"] = TestAdminHmacSecret
            });
        });
    }
}
