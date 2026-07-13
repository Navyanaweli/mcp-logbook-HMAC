namespace McpLogbookApi.Models;

// One row per external HMAC client request attempt (allowed or denied).
// Separate from the internal AuditService log -- exclusively for external
// clients, queryable only via the admin-only endpoint in AuditController.
public record ExternalClientAuditEntry(
    int Id,
    Guid? ClientId,
    int? RequestedCompanyId,
    string ToolName,
    bool Allowed,
    string? DenialReason,
    DateTime Timestamp
);
