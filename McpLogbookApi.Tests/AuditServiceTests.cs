using McpLogbookApi.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace McpLogbookApi.Tests;

public class AuditServiceTests
{
    private readonly AuditService _auditService;

    // Sets up AuditService with empty file path
    public AuditServiceTests()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AuditLog:FilePath"] = "" // empty = no file writing during tests
            })
            .Build();

        _auditService = new AuditService(new NoOpLogger(), config);
    }

    // Silences log output during tests
    private sealed class NoOpLogger : ILogger<AuditService>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) { }
    }

    // Verifies single entry is stored correctly
    [Fact]
    public void Log_AddsEntryToList()
    {
        _auditService.Log("alice", "Administrator", "nordic-shipping", "claude-3",
            "Accessed /api/mcp/admin", "Authorized", "Success", "GET", 200, 5);

        var logs = _auditService.GetLogs();

        Assert.Single(logs);
        Assert.Equal("alice",           logs[0].User);
        Assert.Equal("Administrator",   logs[0].Role);
        Assert.Equal("nordic-shipping", logs[0].TenantId);
        Assert.Equal("claude-3",        logs[0].ClientId);
        Assert.Equal("Authorized",      logs[0].AuthResult);
        Assert.Equal("Success",         logs[0].ExecutionStatus);
        Assert.Equal("GET",             logs[0].HttpMethod);
        Assert.Equal(200,               logs[0].StatusCode);
    }

    // Ensures cross-tenant logs are excluded
    [Fact]
    public void GetLogsForTenant_ReturnsOnlyMatchingTenant()
    {
        _auditService.Log("alice",   "Administrator", "nordic-shipping",  "claude-3", "Accessed /api/mcp/admin",          "Authorized", "Success", "GET", 200, 3);
        _auditService.Log("charlie", "VesselUser",    "pacific-maritime", "gpt-4",   "Accessed /api/mcp/vessel",         "Authorized", "Success", "GET", 200, 4);
        _auditService.Log("bob",     "Superintendent","nordic-shipping",  "claude-3", "Accessed /api/mcp/superintendent", "Authorized", "Success", "GET", 200, 2);

        var logs = _auditService.GetLogsForTenant("nordic-shipping");

        Assert.Equal(2, logs.Count);
        Assert.All(logs, l => Assert.Equal("nordic-shipping", l.TenantId));
    }

    // Returns empty list for unknown tenant
    [Fact]
    public void GetLogsForTenant_ReturnsEmpty_WhenNoMatch()
    {
        _auditService.Log("alice", "Administrator", "nordic-shipping", "claude-3", "Accessed /api/mcp/admin", "Authorized", "Success", "GET", 200, 1);

        var logs = _auditService.GetLogsForTenant("unknown-tenant");

        Assert.Empty(logs);
    }

    // Confirms all logged entries are returned
    [Fact]
    public void GetLogs_ReturnsAllLogs()
    {
        _auditService.Log("alice",   "Administrator", "nordic-shipping",  "claude-3", "Accessed /api/mcp/admin",  "Authorized", "Success", "GET", 200, 2);
        _auditService.Log("charlie", "VesselUser",    "pacific-maritime", "gpt-4",   "Accessed /api/mcp/vessel", "Authorized", "Success", "GET", 200, 3);

        var logs = _auditService.GetLogs();

        Assert.Equal(2, logs.Count);
    }
}
