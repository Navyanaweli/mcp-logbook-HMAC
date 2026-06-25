namespace McpLogbookApi.Services;

public class AuditService
{
    private readonly List<AuditEntry> _logs = [];

    public void Log(string user, string role, string tenantId, string action,
                    string authResult, string executionStatus)
    {
        _logs.Add(new AuditEntry(
            user,
            role,
            tenantId,
            action,
            authResult,
            executionStatus,
            DateTime.UtcNow
        ));
    }

    public List<AuditEntry> GetLogs() => _logs;

    public List<AuditEntry> GetLogsForTenant(string tenantId) =>
        _logs.Where(l => l.TenantId == tenantId).ToList();
}

public record AuditEntry(
    string User,
    string Role,
    string TenantId,
    string Action,
    string AuthResult,       // "Authorized" / "Denied"
    string ExecutionStatus,  // "Success" / "NotFound" / "Error"
    DateTime Timestamp
);