using System.Security.Claims;
using McpLogbookApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace McpLogbookApi.Controllers;

// Exposes audit log read endpoints
[ApiController]
[Route("api/audit")]
[Authorize]
public class AuditController : ControllerBase
{
    private readonly AuditService _audit;

    // Reads tenant and role from Entra ID JWT claims
    private string TenantId => User.FindFirstValue("tid") ?? "unknown";
    private string Role     => User.FindFirstValue(ClaimTypes.Role) ?? "unknown";

    public AuditController(AuditService audit)
    {
        _audit = audit;
    }

    // Admin only — returns every audit log
    [HttpGet("all")]
    [Authorize(Policy = "AdminOnly")]
    public IActionResult GetAllLogs()
    {
        return Ok(_audit.GetLogs());
    }

    // Superintendent+ — own tenant logs only
    [HttpGet("my-tenant")]
    [Authorize(Policy = "SuperintendentUp")]
    public IActionResult GetMyTenantLogs()
    {
        return Ok(_audit.GetLogsForTenant(TenantId));
    }
}
