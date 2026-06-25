using McpLogbookApi.Services;

namespace McpLogbookApi.Tests;

public class AuditServiceTests
{
    private readonly AuditService _auditService = new();

    [Fact]
    public void Log_AddsEntryToList()
    {
        _auditService.Log("alice", "Administrator", "nordic-shipping",
            "Accessed /api/mcp/admin", "Authorized", "Success");

        var logs = _auditService.GetLogs();

        Assert.Single(logs);
        Assert.Equal("alice",           logs[0].User);
        Assert.Equal("Administrator",   logs[0].Role);
        Assert.Equal("nordic-shipping", logs[0].TenantId);
        Assert.Equal("Authorized",      logs[0].AuthResult);
        Assert.Equal("Success",         logs[0].ExecutionStatus);
    }

    [Fact]
    public void GetLogsForTenant_ReturnsOnlyMatchingTenant()
    {
        _auditService.Log("alice",   "Administrator", "nordic-shipping",  "Accessed /api/mcp/admin",          "Authorized", "Success");
        _auditService.Log("charlie", "VesselUser",    "pacific-maritime", "Accessed /api/mcp/vessel",         "Authorized", "Success");
        _auditService.Log("bob",     "Superintendent","nordic-shipping",  "Accessed /api/mcp/superintendent", "Authorized", "Success");

        var logs = _auditService.GetLogsForTenant("nordic-shipping");

        Assert.Equal(2, logs.Count);
        Assert.All(logs, l => Assert.Equal("nordic-shipping", l.TenantId));
    }

    [Fact]
    public void GetLogsForTenant_ReturnsEmpty_WhenNoMatch()
    {
        _auditService.Log("alice", "Administrator", "nordic-shipping", "Accessed /api/mcp/admin", "Authorized", "Success");

        var logs = _auditService.GetLogsForTenant("unknown-tenant");

        Assert.Empty(logs);
    }

    [Fact]
    public void GetLogs_ReturnsAllLogs()
    {
        _auditService.Log("alice",   "Administrator", "nordic-shipping",  "Accessed /api/mcp/admin",  "Authorized", "Success");
        _auditService.Log("charlie", "VesselUser",    "pacific-maritime", "Accessed /api/mcp/vessel", "Authorized", "Success");

        var logs = _auditService.GetLogs();

        Assert.Equal(2, logs.Count);
    }
}