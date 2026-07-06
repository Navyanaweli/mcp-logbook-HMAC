namespace McpLogbookApi.Models;

// Mirrors ShipLogTable, joined with Ships for a readable name
public record ShipLogEntry(int ShipLogId, int ShipId, string ShipName, string LogText, string LogDate);
