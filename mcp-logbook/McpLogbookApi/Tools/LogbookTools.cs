using System.ComponentModel;
using System.Security.Claims;
using McpLogbookApi.Models;
using McpLogbookApi.Services;
using ModelContextProtocol.Server;

namespace McpLogbookApi.Tools;

// Read-only MCP tools
// Reuses LogbookRepository and the same ship-scoped authorization as McpController.
[McpServerToolType]
public class LogbookTools
{
    private readonly LogbookRepository _repo;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public LogbookTools(LogbookRepository repo, IHttpContextAccessor httpContextAccessor)
    {
        _repo = repo;
        _httpContextAccessor = httpContextAccessor;
    }

    // Literal claim type, not ClaimTypes.Name — see the same note in McpController
    private string Username =>
        _httpContextAccessor.HttpContext?.User.FindFirstValue("preferred_username") ?? "unknown";

    [McpServerTool, Description("Lists logbook entries for the ships the current user is assigned to.")]
    public IReadOnlyList<ShipLogEntry> GetLogbookEntries()
    {
        var shipIds = _repo.GetAssignedShipIds(Username);
        return _repo.GetShipLogs(shipIds);
    }

    [McpServerTool, Description("Searches logbook entries by log text, scoped to the ships the current user is assigned to.")]
    public IReadOnlyList<ShipLogEntry> SearchLogbookEntries(
        [Description("Text to search for within the log entry text.")] string query)
    {
        var shipIds = _repo.GetAssignedShipIds(Username);
        return _repo.GetShipLogs(shipIds)
            .Where(entry => entry.LogText.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    [McpServerTool, Description("Gets a single logbook entry by id, only if the current user is assigned to its ship.")]
    public ShipLogEntry? GetLogbookEntryById(
        [Description("The logbook entry id.")] int id)
    {
        var shipIds = _repo.GetAssignedShipIds(Username);
        var entry = _repo.GetShipLogById(id);

        // Returns null both when the entry doesn't exist and when it exists but belongs to
        // a ship the user isn't assigned to — mirrors McpController's identical-404 behavior
        // so a caller can never tell "no such log" from "exists, not yours"
        return entry is not null && shipIds.Contains(entry.ShipId) ? entry : null;
    }
}
