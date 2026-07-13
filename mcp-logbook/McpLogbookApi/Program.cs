using System.Text.Json;
using McpLogbookApi.Data;
using McpLogbookApi.Middleware;
using McpLogbookApi.Models;
using McpLogbookApi.Services;
using Microsoft.Data.Sqlite;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// Resolves a relative SQLite path against the app's base directory so it's
// independent of the process's current working directory (dotnet run, IDE
// launch, and tests can all have different CWDs otherwise)
var rawConnectionString = builder.Configuration.GetConnectionString("LogbookDb")
    ?? "Data Source=Data/logbook.db";
var sqliteBuilder = new SqliteConnectionStringBuilder(rawConnectionString);
if (!Path.IsPathRooted(sqliteBuilder.DataSource))
    sqliteBuilder.DataSource = Path.Combine(AppContext.BaseDirectory, sqliteBuilder.DataSource);
builder.Configuration["ConnectionStrings:LogbookDb"] = sqliteBuilder.ConnectionString;

// ── Core Services ──────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddControllers();
builder.Services.AddSingleton<LogbookRepository>();
// Lets MCP tools read the current request's resolved HMAC client identity
// (HmacAuthContext), since tools have no HttpContext of their own
builder.Services.AddHttpContextAccessor();

// ── External (HMAC) client support -- the only authentication model ────
// Encrypts/decrypts Client.EncryptedSecret; same key ring used by
// ClientOnboardingService (encrypt) and HmacAuthenticationMiddleware (decrypt)
builder.Services.AddDataProtection();
builder.Services.AddSingleton<ExternalClientRepository>();
builder.Services.AddSingleton<ClientOnboardingService>();
builder.Services.AddSingleton<AccessScopeResolver>();

// ── MCP server — read-only tools from Tools/LogbookTools.cs ──
builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.Stateless = true)
    .WithToolsFromAssembly();

// ── CORS — Angular dev server ──────────────────────────────────
builder.Services.AddCors(options =>
{
    options.AddPolicy("AngularDev", policy => policy
        .WithOrigins("http://localhost:4200")
        .AllowAnyHeader()
        .AllowAnyMethod());
});

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "MCP Logbook API", Version = "v1" });
});

// ── Pipeline ───────────────────────────────────────────────────
var app = builder.Build();

// Creates and seeds the SQLite database on first run
DatabaseInitializer.EnsureCreated(
    builder.Configuration.GetConnectionString("LogbookDb")!,
    Path.Combine(AppContext.BaseDirectory, "Data"));

// ── Demo/local-testing-only setup mode ──────────────────────────
// `dotnet run -- --seed-demo-client` onboards one demo Company + Client for the
// McpHmacProxy / Claude Desktop demo, then exits without starting Kestrel. Runs
// in-process (not a separate tool) so it shares this app's exact DataProtection key
// ring -- onboarding from a different process could encrypt a secret this app's own
// HmacAuthenticationMiddleware then fails to decrypt.
if (args.Contains("--seed-demo-client"))
{
    SeedDemoClient(app.Services);
    return;
}

// `dotnet run -- --seed-demo-client-restricted` onboards a second demo Client scoped to
// only ONE company (reusing the seeded "Ocean Star Shipping Co." if present), to
// demonstrate/exercise company-scoped restriction rather than the broad
// see-every-ship demo client above. Saved to a separate credentials file so it
// never overwrites the broad one.
if (args.Contains("--seed-demo-client-restricted"))
{
    SeedDemoClientRestricted(app.Services);
    return;
}

// Swagger only in development environment
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AngularDev");

// Enforces HMAC auth on /mcp and /api/mcp/* (external clients) and, separately, on
// /api/admin/* and /api/audit/external (the single admin identity) -- see
// HmacAuthenticationMiddleware and AdminHmacAuthenticationMiddleware. Everything else
// (Swagger) passes through untouched.
app.UseMiddleware<HmacAuthenticationMiddleware>();
app.UseMiddleware<AdminHmacAuthenticationMiddleware>();

app.MapControllers();

app.MapMcp("/mcp");

app.Run();

// Company reused by both demo commands below. Since each ship now belongs to exactly one
// company (Ships.CompanyId), there's no longer a company that owns "every ship" to grant a
// new demo company broad access to -- so both the broad and restricted demo clients are
// scoped to this same seeded company, differing only in client name/credentials file.
const string DemoCompanyName = "Ocean Star Shipping Co.";

// Onboards one demo Client for local HMAC testing via McpHmacProxy, scoped to
// DemoCompanyName. Idempotent across runs: if the credentials file already exists, prints
// it instead of minting a new client.
static void SeedDemoClient(IServiceProvider services)
{
    var credentialsPath = Path.Combine(AppContext.BaseDirectory, "Data", "demo-client-credentials.json");

    if (File.Exists(credentialsPath))
    {
        var existing = JsonSerializer.Deserialize<DemoClientCredentials>(File.ReadAllText(credentialsPath));
        Console.WriteLine($"Demo client credentials already exist at {credentialsPath} -- not regenerating.");
        Console.WriteLine($"  ClientId:  {existing!.ClientId}");
        Console.WriteLine($"  CompanyId: {existing.CompanyId}");
        return;
    }

    var logbookRepo = services.GetRequiredService<LogbookRepository>();
    var clientRepo = services.GetRequiredService<ExternalClientRepository>();
    var onboarding = services.GetRequiredService<ClientOnboardingService>();

    var companyId = clientRepo.GetCompanyIdByName(DemoCompanyName);
    if (companyId is null)
    {
        companyId = clientRepo.InsertCompany(DemoCompanyName);
        Console.WriteLine($"'{DemoCompanyName}' not found -- created it as a new company (CompanyId {companyId}, no ships assigned to it yet).");
    }
    else
    {
        Console.WriteLine($"Reusing existing company '{DemoCompanyName}' (CompanyId {companyId}).");
    }

    var result = onboarding.CreateClient("Claude Desktop Demo Client", companyId.Value, DateTime.UtcNow.AddYears(1));
    var shipCount = logbookRepo.GetShipIdsForCompanies([companyId.Value]).Count;

    var credentials = new DemoClientCredentials(result.ClientId, result.RawSecret, companyId.Value, DateTime.UtcNow);
    File.WriteAllText(credentialsPath, JsonSerializer.Serialize(credentials, new JsonSerializerOptions { WriteIndented = true }));

    Console.WriteLine("Demo client created.");
    Console.WriteLine($"  ClientId:     {result.ClientId}");
    Console.WriteLine($"  SharedSecret: {result.RawSecret}");
    Console.WriteLine($"  CompanyId:    {companyId} ('{DemoCompanyName}', access to {shipCount} ship(s))");
    Console.WriteLine($"  Saved to:     {credentialsPath}");
    Console.WriteLine("This is the only time the shared secret will be printed -- it is stored encrypted from here on.");
}

// Onboards a second demo Client, also scoped to DemoCompanyName -- kept as a separate
// command (and separate credentials file) from SeedDemoClient above for anyone who wants
// two distinct demo client identities to test with, even though both currently resolve to
// the same company. Idempotent across runs, same as SeedDemoClient.
static void SeedDemoClientRestricted(IServiceProvider services)
{
    var credentialsPath = Path.Combine(AppContext.BaseDirectory, "Data", "demo-client-credentials-restricted.json");

    if (File.Exists(credentialsPath))
    {
        var existing = JsonSerializer.Deserialize<DemoClientCredentials>(File.ReadAllText(credentialsPath));
        Console.WriteLine($"Restricted demo client credentials already exist at {credentialsPath} -- not regenerating.");
        Console.WriteLine($"  ClientId:  {existing!.ClientId}");
        Console.WriteLine($"  CompanyId: {existing.CompanyId}");
        return;
    }

    var logbookRepo = services.GetRequiredService<LogbookRepository>();
    var clientRepo = services.GetRequiredService<ExternalClientRepository>();
    var onboarding = services.GetRequiredService<ClientOnboardingService>();

    var companyId = clientRepo.GetCompanyIdByName(DemoCompanyName);
    if (companyId is null)
    {
        companyId = clientRepo.InsertCompany(DemoCompanyName);
        Console.WriteLine($"'{DemoCompanyName}' not found -- created it as a new company (CompanyId {companyId}, no ships assigned to it yet).");
    }
    else
    {
        Console.WriteLine($"Reusing existing company '{DemoCompanyName}' (CompanyId {companyId}).");
    }

    var result = onboarding.CreateClient("Claude Desktop Restricted Demo Client", companyId.Value, DateTime.UtcNow.AddYears(1));
    var shipCount = logbookRepo.GetShipIdsForCompanies([companyId.Value]).Count;

    var credentials = new DemoClientCredentials(result.ClientId, result.RawSecret, companyId.Value, DateTime.UtcNow);
    File.WriteAllText(credentialsPath, JsonSerializer.Serialize(credentials, new JsonSerializerOptions { WriteIndented = true }));

    Console.WriteLine("Restricted demo client created.");
    Console.WriteLine($"  ClientId:     {result.ClientId}");
    Console.WriteLine($"  SharedSecret: {result.RawSecret}");
    Console.WriteLine($"  CompanyId:    {companyId} ('{DemoCompanyName}', access to {shipCount} ship(s) only)");
    Console.WriteLine($"  Saved to:     {credentialsPath}");
    Console.WriteLine("This is the only time the shared secret will be printed -- it is stored encrypted from here on.");
}

// Allows WebApplicationFactory in tests to reference this entry point
public partial class Program { }
