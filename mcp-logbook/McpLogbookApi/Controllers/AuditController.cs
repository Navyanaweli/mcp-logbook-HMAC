using System.Security.Claims;
using McpLogbookApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace McpLogbookApi.Controllers;

[ApiController]
[Route("api/audit")]
[Authorize]
public class AuditController : ControllerBase
{
    private readonly AuditService _audit;

    private string TenantId => User.FindFirstValue("TenantId") ?? "unknown";
    private string Role     => User.FindFirstValue(ClaimTypes.Role) ?? "unknown";

    public AuditController(AuditService audit)
    {
        _audit = audit;
    }

    [HttpGet("all")]
    [Authorize(Policy = "AdminOnly")]
    public IActionResult GetAllLogs()
    {
        return Ok(_audit.GetLogs());
    }

    [HttpGet("my-tenant")]
    [Authorize(Policy = "InspectorUp")]
    public IActionResult GetMyTenantLogs()
    {
        return Ok(_audit.GetLogsForTenant(TenantId));
    }
}