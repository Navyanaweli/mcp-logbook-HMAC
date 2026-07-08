namespace McpLogbookApi.Models;

// A ship a user is assigned to, for display (e.g. the dashboard's "authorized ships" list)
public record AssignedShip(int ShipId, string ShipName);
