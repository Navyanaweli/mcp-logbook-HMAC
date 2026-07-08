using System.Diagnostics;
using System.Security.Claims;
using McpLogbookApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace McpLogbookApi.Controllers;

// Read-only logbook access endpoints
[ApiController]
[Route("api/mcp")]
[Authorize]
public class McpController : ControllerBase
{
    private readonly AuditService _audit;
    private readonly LogbookRepository _repo;

    // Extracts identity fields from Entra ID JWT claims. Read the literal claim
    // types directly (not ClaimTypes.Name/Role) — NameClaimType/RoleClaimType on
    // TokenValidationParameters only affect Identity.Name/IsInRole(), not
    // FindFirstValue(ClaimTypes.X), so those would always return "unknown".
    private string TenantId => User.FindFirstValue("tid") ?? "unknown";
    private string Username => User.FindFirstValue("preferred_username") ?? "unknown";
    private string Role     => User.FindFirstValue("roles") ?? "unknown";
    private string ClientId => Request.Headers["X-Client-Id"].FirstOrDefault() ?? "unknown";

    public McpController(AuditService audit, LogbookRepository repo)
    {
        _audit = audit;
        _repo = repo;
    }

    // Current user's identity, role, and authorized ships — for the dashboard header
    [HttpGet("me")]
    [Authorize(Policy = "ReadOnlyUp")]
    public IActionResult Me()
    {
        var ships = _repo.GetAssignedShips(Username);
        return Ok(new { username = Username, role = Role, ships });
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

    // Ship-scoped logbook data — all roles
    [HttpGet("logbooks")]
    [Authorize(Policy = "ReadOnlyUp")]
    public IActionResult GetLogbooks()
    {
        var sw = Stopwatch.StartNew();
        // Filters logbooks to ships the current user is assigned to
        var shipIds = _repo.GetAssignedShipIds(Username);
        var logbooks = _repo.GetShipLogs(shipIds);
        sw.Stop();

        // Returns 404 if the user has no assigned ships
        if (logbooks.Count == 0)
        {
            _audit.Log(Username, Role, TenantId, ClientId, "Accessed /api/mcp/logbooks", "Authorized", "NotFound", Request.Method, 404, sw.ElapsedMilliseconds);
            return NotFound(new { message = "No logbooks found for your assigned ships." });
        }

        _audit.Log(Username, Role, TenantId, ClientId, "Accessed /api/mcp/logbooks", "Authorized", "Success", Request.Method, 200, sw.ElapsedMilliseconds);
        return Ok(new { accessedBy = Username, shipIds, count = logbooks.Count, logbooks });
    }

    // Single logbook entry — all roles, but only if the entry's ship is assigned to the user
    [HttpGet("logbooks/{id:int}")]
    [Authorize(Policy = "ReadOnlyUp")]
    public IActionResult GetLogbookById(int id)
    {
        var sw = Stopwatch.StartNew();
        var shipIds = _repo.GetAssignedShipIds(Username);
        var entry = _repo.GetShipLogById(id);
        sw.Stop();

        // Identical response whether the entry doesn't exist or belongs to a ship the
        // user isn't assigned to — a caller must never be able to tell the two apart
        if (entry is null || !shipIds.Contains(entry.ShipId))
        {
            _audit.Log(Username, Role, TenantId, ClientId, $"Accessed /api/mcp/logbooks/{id}", "Authorized", "NotFound", Request.Method, 404, sw.ElapsedMilliseconds);
            return NotFound(new { message = "Logbook entry not found." });
        }

        _audit.Log(Username, Role, TenantId, ClientId, $"Accessed /api/mcp/logbooks/{id}", "Authorized", "Success", Request.Method, 200, sw.ElapsedMilliseconds);
        return Ok(entry);
    }
}
