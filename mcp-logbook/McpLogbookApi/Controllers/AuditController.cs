using McpLogbookApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace McpLogbookApi.Controllers;

// Admin-only read access to the external HMAC client audit log. Auth is enforced
// upstream by AdminHmacAuthenticationMiddleware -- no [Authorize]/filter needed here.
[ApiController]
[Route("api/audit")]
public class AuditController : ControllerBase
{
    private readonly ExternalClientRepository _externalClientRepo;

    public AuditController(ExternalClientRepository externalClientRepo)
    {
        _externalClientRepo = externalClientRepo;
    }

    // Exclusively external HMAC client attempts (allowed and denied).
    // Optional filters: clientId, date range, allowed/denied status.
    [HttpGet("external")]
    public IActionResult GetExternalClientLogs(
        [FromQuery] Guid? clientId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] bool? allowed)
    {
        return Ok(_externalClientRepo.GetAuditLogs(clientId, from, to, allowed));
    }
}
