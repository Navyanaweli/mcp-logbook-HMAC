using System.Diagnostics;
using System.Security.Claims;
using McpLogbookApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace McpLogbookApi.Controllers;

// Logbook access endpoints by role
[ApiController]
[Route("api/mcp")]
[Authorize]
public class McpController : ControllerBase
{
    private readonly AuditService _audit;

    // Extracts identity fields from Entra ID JWT claims
    private string TenantId => User.FindFirstValue("tid") ?? "unknown";
    private string Username => User.FindFirstValue(ClaimTypes.Name) ?? "unknown";
    private string Role     => User.FindFirstValue(ClaimTypes.Role) ?? "unknown";
    private string ClientId => Request.Headers["X-Client-Id"].FirstOrDefault() ?? "unknown";

    public McpController(AuditService audit)
    {
        _audit = audit;
    }

    // Administrator only — manage tools, users, tenants
    [HttpGet("admin")]
    [Authorize(Policy = "AdminOnly")]
    public IActionResult AdminOnly()
    {
        var sw = Stopwatch.StartNew();
        _audit.Log(Username, Role, TenantId, ClientId, "Accessed /api/mcp/admin", "Authorized", "Success", Request.Method, 200, sw.ElapsedMilliseconds);
        return Ok(new { message = "Administrator access granted. Full system control.", accessedBy = Username, tenant = TenantId });
    }

    // Superintendent + above — inspect and approve logbooks
    [HttpGet("superintendent")]
    [Authorize(Policy = "SuperintendentUp")]
    public IActionResult SuperintendentAccess()
    {
        var sw = Stopwatch.StartNew();
        _audit.Log(Username, Role, TenantId, ClientId, "Accessed /api/mcp/superintendent", "Authorized", "Success", Request.Method, 200, sw.ElapsedMilliseconds);
        return Ok(new { message = "Superintendent access granted. Can inspect and approve logbooks.", accessedBy = Username, tenant = TenantId });
    }

    // Vessel User + above — submit and edit logbook entries
    [HttpGet("vessel")]
    [Authorize(Policy = "VesselUserUp")]
    public IActionResult VesselUserAccess()
    {
        var sw = Stopwatch.StartNew();
        _audit.Log(Username, Role, TenantId, ClientId, "Accessed /api/mcp/vessel", "Authorized", "Success", Request.Method, 200, sw.ElapsedMilliseconds);
        return Ok(new { message = "Vessel User access granted. Can submit and edit logbook entries.", accessedBy = Username, tenant = TenantId });
    }

    // All roles — read-only view of logbooks
    [HttpGet("readonly")]
    [Authorize(Policy = "ReadOnlyUp")]
    public IActionResult ReadOnlyAccess()
    {
        var sw = Stopwatch.StartNew();
        _audit.Log(Username, Role, TenantId, ClientId, "Accessed /api/mcp/readonly", "Authorized", "Success", Request.Method, 200, sw.ElapsedMilliseconds);
        return Ok(new { message = "Read-Only access granted. Can view logbook records.", accessedBy = Username, tenant = TenantId });
    }

    // Tenant-isolated logbook data — all roles
    [HttpGet("logbooks")]
    [Authorize(Policy = "ReadOnlyUp")]
    public IActionResult GetLogbooks()
    {
        var sw = Stopwatch.StartNew();
        // Filters logbooks to current user's tenant
        var logbooks = GetMockLogbooks().Where(l => l.TenantId == TenantId).ToList();
        sw.Stop();

        // Returns 404 if tenant has no logbooks
        if (!logbooks.Any())
        {
            _audit.Log(Username, Role, TenantId, ClientId, "Accessed /api/mcp/logbooks", "Authorized", "NotFound", Request.Method, 404, sw.ElapsedMilliseconds);
            return NotFound(new { message = "No logbooks found for your tenant.", tenant = TenantId });
        }

        _audit.Log(Username, Role, TenantId, ClientId, "Accessed /api/mcp/logbooks", "Authorized", "Success", Request.Method, 200, sw.ElapsedMilliseconds);
        return Ok(new { tenant = TenantId, accessedBy = Username, count = logbooks.Count, logbooks });
    }

    // Sample data spanning three tenants
    private static List<LogbookEntry> GetMockLogbooks() =>
    [
        new("LOG-001", "Vessel Alpha - Oil Record Book",  "nordic-shipping"),
        new("LOG-002", "Vessel Beta - Garbage Record",    "nordic-shipping"),
        new("LOG-003", "Vessel Gamma - Oil Record Book",  "pacific-maritime"),
        new("LOG-004", "Vessel Delta - Cargo Record",     "pacific-maritime"),
        new("LOG-005", "Vessel Epsilon - Oil Record Book","atlantic-fleet"),
    ];
}

// Simple logbook entry data shape
record LogbookEntry(string Id, string Title, string TenantId);
