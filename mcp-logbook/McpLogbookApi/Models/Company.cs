namespace McpLogbookApi.Models;

// An external-client-facing company. Each ship belongs to exactly one company
// (Ships.CompanyId), which is what makes it visible to external clients.
public record Company(int CompanyId, string CompanyName);
