using System.Security.Claims;
using McpLogbookApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace McpLogbookApi.Controllers;

[ApiController]
[Route("api/mcp")]
[Authorize]
public class McpController : ControllerBase
{
    private readonly AuditService _audit;

    private string TenantId => User.FindFirstValue("TenantId") ?? "unknown";
    private string Username => User.FindFirstValue(ClaimTypes.Name) ?? "unknown";
    private string Role     => User.FindFirstValue(ClaimTypes.Role) ?? "unknown";

    public McpController(AuditService audit)
    {
        _audit = audit;
    }

    // Administrator only — manage tools, users, tenants
    [HttpGet("admin")]
    [Authorize(Policy = "AdminOnly")]
    public IActionResult AdminOnly()
    {
        _audit.Log(Username, Role, TenantId, "Accessed /api/mcp/admin", "Authorized", "Success");
        return Ok(new { message = "Administrator access granted. Full system control.", accessedBy = Username, tenant = TenantId });
    }

    // Superintendent + above — inspect and approve logbooks
    [HttpGet("superintendent")]
    [Authorize(Policy = "SuperintendentUp")]
    public IActionResult SuperintendentAccess()
    {
        _audit.Log(Username, Role, TenantId, "Accessed /api/mcp/superintendent", "Authorized", "Success");
        return Ok(new { message = "Superintendent access granted. Can inspect and approve logbooks.", accessedBy = Username, tenant = TenantId });
    }

    // Vessel User + above — submit and edit logbook entries
    [HttpGet("vessel")]
    [Authorize(Policy = "VesselUserUp")]
    public IActionResult VesselUserAccess()
    {
        _audit.Log(Username, Role, TenantId, "Accessed /api/mcp/vessel", "Authorized", "Success");
        return Ok(new { message = "Vessel User access granted. Can submit and edit logbook entries.", accessedBy = Username, tenant = TenantId });
    }

    // All roles — read-only view of logbooks
    [HttpGet("readonly")]
    [Authorize(Policy = "ReadOnlyUp")]
    public IActionResult ReadOnlyAccess()
    {
        _audit.Log(Username, Role, TenantId, "Accessed /api/mcp/readonly", "Authorized", "Success");
        return Ok(new { message = "Read-Only access granted. Can view logbook records.", accessedBy = Username, tenant = TenantId });
    }

    // Tenant-isolated logbook data — all roles
    [HttpGet("logbooks")]
    [Authorize(Policy = "ReadOnlyUp")]
    public IActionResult GetLogbooks()
    {
        _audit.Log(Username, Role, TenantId, "Accessed /api/mcp/logbooks", "Authorized", "Success");

        var logbooks = GetMockLogbooks()
            .Where(l => l.TenantId == TenantId)
            .ToList();

        if (!logbooks.Any())
        {
            _audit.Log(Username, Role, TenantId, "Accessed /api/mcp/logbooks", "Authorized", "NotFound");
            return NotFound(new { message = "No logbooks found for your tenant.", tenant = TenantId });
        }

        return Ok(new { tenant = TenantId, accessedBy = Username, count = logbooks.Count, logbooks });
    }

    private static List<LogbookEntry> GetMockLogbooks() =>
    [
        new("LOG-001", "Vessel Alpha - Oil Record Book",  "nordic-shipping"),
        new("LOG-002", "Vessel Beta - Garbage Record",    "nordic-shipping"),
        new("LOG-003", "Vessel Gamma - Oil Record Book",  "pacific-maritime"),
        new("LOG-004", "Vessel Delta - Cargo Record",     "pacific-maritime"),
        new("LOG-005", "Vessel Epsilon - Oil Record Book","atlantic-fleet"),
    ];
}

record LogbookEntry(string Id, string Title, string TenantId);