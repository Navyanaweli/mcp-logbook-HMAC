using McpLogbookApi.Middleware;
using McpLogbookApi.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// ── Core Services ──────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddControllers();
// Singleton so audit logs persist across requests
builder.Services.AddSingleton<AuditService>();

// ── OAuth 2.0 / Microsoft Entra ID Authentication ─────────────
var azureAd = builder.Configuration.GetSection("AzureAd");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Fetches signing keys automatically from Entra ID discovery endpoint
        options.Authority = azureAd["Authority"];
        options.Audience  = azureAd["ClientId"];
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer    = true,
            ValidateAudience  = true,
            ValidateLifetime  = true,
            // Maps preferred_username claim → ClaimTypes.Name
            NameClaimType = "preferred_username",
            // Maps roles claim → ClaimTypes.Role (Entra ID app roles)
            RoleClaimType = "roles"
        };
    });

// Role-based policies per access level
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly",        policy => policy.RequireRole("Administrator"));
    options.AddPolicy("SuperintendentUp", policy => policy.RequireRole("Administrator", "Superintendent"));
    options.AddPolicy("VesselUserUp",     policy => policy.RequireRole("Administrator", "Superintendent", "VesselUser"));
    options.AddPolicy("ReadOnlyUp",       policy => policy.RequireRole("Administrator", "Superintendent", "VesselUser", "ReadOnlyUser"));
});

// ── Swagger with Bearer token support ─────────────────────────
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "MCP Logbook API", Version = "v1" });

    // Adds Bearer token input to Swagger UI
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name         = "Authorization",
        Type         = SecuritySchemeType.Http,
        Scheme       = "Bearer",
        BearerFormat = "JWT",
        In           = ParameterLocation.Header,
        Description  = "Paste your Entra ID access token. Example: Bearer eyJhbGci..."
    });

    // Applies Bearer scheme to all endpoints
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id   = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// ── Pipeline ───────────────────────────────────────────────────
var app = builder.Build();

// Swagger only in development environment
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Must wrap auth so 401/403 responses are captured
app.UseMiddleware<ObservabilityMiddleware>();

// Authentication must come before authorization
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

// Allows WebApplicationFactory in tests to reference this entry point
public partial class Program { }
