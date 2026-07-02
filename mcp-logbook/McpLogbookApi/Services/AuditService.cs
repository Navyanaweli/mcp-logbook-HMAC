using System.Text.Json;

namespace McpLogbookApi.Services;

public class AuditService
{
    // In-memory list of all audit entries
    private readonly List<AuditEntry> _logs = [];
    private readonly ILogger<AuditService> _logger;
    // Optional file path for persistent logging
    private readonly string? _logFilePath;
    // Lock prevents concurrent file write conflicts
    private readonly object _fileLock = new();

    // Reads file path from app configuration
    public AuditService(ILogger<AuditService> logger, IConfiguration config)
    {
        _logger = logger;
        _logFilePath = config["AuditLog:FilePath"];
    }

    // Records one audit entry per request
    public void Log(string user, string role, string tenantId, string clientId,
                    string action, string authResult, string executionStatus,
                    string httpMethod, int statusCode, long durationMs)
    {
        // Builds entry with current UTC timestamp
        var entry = new AuditEntry(user, role, tenantId, clientId, action,
            authResult, executionStatus, httpMethod, statusCode, durationMs, DateTime.UtcNow);

        _logs.Add(entry);

        // Emits structured log for observability
        _logger.LogInformation(
            "Audit: User={User} Role={Role} TenantId={TenantId} ClientId={ClientId} " +
            "Action={Action} AuthResult={AuthResult} Status={ExecutionStatus} " +
            "HttpMethod={HttpMethod} StatusCode={StatusCode} DurationMs={DurationMs}",
            user, role, tenantId, clientId, action, authResult, executionStatus,
            httpMethod, statusCode, durationMs);

        // Appends JSON line to audit file
        if (!string.IsNullOrEmpty(_logFilePath))
        {
            var line = JsonSerializer.Serialize(entry) + Environment.NewLine;
            lock (_fileLock)
                File.AppendAllText(_logFilePath, line);
        }
    }

    // Returns full in-memory log list
    public List<AuditEntry> GetLogs() => _logs;

    // Filters logs by tenant ID
    public List<AuditEntry> GetLogsForTenant(string tenantId) =>
        _logs.Where(l => l.TenantId == tenantId).ToList();
}

// Immutable record for a single audit event
public record AuditEntry(
    string User,
    string Role,
    string TenantId,
    string ClientId,         // which LLM/client sent the request (X-Client-Id header)
    string Action,
    string AuthResult,       // "Authorized" / "Denied" / "Unauthenticated"
    string ExecutionStatus,  // "Success" / "NotFound" / "Error" / "Blocked"
    string HttpMethod,
    int    StatusCode,
    long   DurationMs,
    DateTime Timestamp
);
