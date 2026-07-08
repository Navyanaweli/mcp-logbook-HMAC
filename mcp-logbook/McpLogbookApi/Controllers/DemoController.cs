using McpLogbookApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace McpLogbookApi.Controllers;

// ⚠️ DEMO/LOCAL TESTING ONLY — deliberately unauthenticated (no [Authorize],
// no token of any kind). The caller supplies which seeded user they are
// acting as directly, in place of a validated JWT identity. This exists so
// the Angular UI can be exercised without an Entra ID App Registration.
// It reuses the exact same LogbookRepository ship-scoping logic as the real,
// OAuth-protected McpController — nothing about authorization is mocked,
// only the "who is calling" step. Do not expose this in a real deployment.
[ApiController]
[Route("api/demo")]
public class DemoController : ControllerBase
{
    private readonly LogbookRepository _repo;

    public DemoController(LogbookRepository repo)
    {
        _repo = repo;
    }

    // Seeded users, for the login dropdown
    [HttpGet("users")]
    public IActionResult GetUsers()
    {
        return Ok(_repo.GetAllUsers());
    }

    // Identity/role/ships for the selected demo user
    [HttpGet("me")]
    public IActionResult Me([FromQuery] string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return BadRequest(new { message = "email is required." });

        var ships = _repo.GetAssignedShips(email);
        return Ok(new { username = email, role = "Demo", ships });
    }

    // Ship-scoped logbook list for the selected demo user
    [HttpGet("logbooks")]
    public IActionResult GetLogbooks([FromQuery] string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return BadRequest(new { message = "email is required." });

        var shipIds = _repo.GetAssignedShipIds(email);
        var logbooks = _repo.GetShipLogs(shipIds);

        if (logbooks.Count == 0)
            return NotFound(new { message = "No logbooks found for your assigned ships." });

        return Ok(new { accessedBy = email, shipIds, count = logbooks.Count, logbooks });
    }

    // Single logbook entry — same identical-response behavior as McpController:
    // "doesn't exist" and "exists but not your ship" are indistinguishable
    [HttpGet("logbooks/{id:int}")]
    public IActionResult GetLogbookById(int id, [FromQuery] string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return BadRequest(new { message = "email is required." });

        var shipIds = _repo.GetAssignedShipIds(email);
        var entry = _repo.GetShipLogById(id);

        if (entry is null || !shipIds.Contains(entry.ShipId))
            return NotFound(new { message = "Logbook entry not found or you do not have access." });

        return Ok(entry);
    }
}
