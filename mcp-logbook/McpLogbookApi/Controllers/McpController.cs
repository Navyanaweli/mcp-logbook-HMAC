using McpLogbookApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace McpLogbookApi.Controllers;

// Read-only logbook access endpoints. Auth is enforced upstream by
// HmacAuthenticationMiddleware, which requires valid HMAC headers on every
// request under /api/mcp -- no [Authorize] needed here.
[ApiController]
[Route("api/mcp")]
public class McpController : ControllerBase
{
    private readonly LogbookRepository _repo;
    private readonly AccessScopeResolver _scopeResolver;

    public McpController(LogbookRepository repo, AccessScopeResolver scopeResolver)
    {
        _repo = repo;
        _scopeResolver = scopeResolver;
    }

    // Ship-scoped logbook data
    [HttpGet("logbooks")]
    public IActionResult GetLogbooks()
    {
        // Filters logbooks to the ships the current HMAC client's company(ies) can access
        var shipIds = _scopeResolver.GetAccessibleShipIds(HttpContext);
        var logbooks = _repo.GetShipLogs(shipIds);

        // Returns 404 if the client has no accessible ships
        if (logbooks.Count == 0)
            return NotFound(new { message = "No logbooks found for your accessible ships." });

        return Ok(new { shipIds, count = logbooks.Count, logbooks });
    }

    // Single logbook entry -- only if the entry's ship is accessible to the caller
    [HttpGet("logbooks/{id:int}")]
    public IActionResult GetLogbookById(int id)
    {
        var shipIds = _scopeResolver.GetAccessibleShipIds(HttpContext);
        var entry = _repo.GetShipLogById(id);

        // Identical response whether the entry doesn't exist or belongs to a ship the
        // caller can't access -- a caller must never be able to tell the two apart
        if (entry is null || !shipIds.Contains(entry.ShipId))
            return NotFound(new { message = "Logbook entry not found." });

        return Ok(entry);
    }
}
