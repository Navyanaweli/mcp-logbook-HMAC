using System.Diagnostics;
using System.Security.Claims;
using McpLogbookApi.Services;

namespace McpLogbookApi.Middleware;

// Logs unauthorized and forbidden requests
public class ObservabilityMiddleware
{
    private readonly RequestDelegate _next;
    private readonly AuditService _audit;

    public ObservabilityMiddleware(RequestDelegate next, AuditService audit)
    {
        _next = next;
        _audit = audit;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Times the full request pipeline
        var stopwatch = Stopwatch.StartNew();
        await _next(context);
        stopwatch.Stop();

        // Only audits 401 and 403 responses
        var statusCode = context.Response.StatusCode;
        if (statusCode is 401 or 403)
        {
            // Extracts identity from Entra ID JWT claims
            var user     = context.User?.FindFirstValue("preferred_username") ?? "anonymous";
            var role     = context.User?.FindFirstValue("roles") ?? "unknown";
            var tenantId = context.User?.FindFirstValue("tid") ?? "unknown";
            var clientId = context.Request.Headers["X-Client-Id"].FirstOrDefault() ?? "unknown";
            var action   = $"{context.Request.Method} {context.Request.Path}";
            // 401 = no token, 403 = insufficient role
            var authResult = statusCode == 401 ? "Unauthenticated" : "Denied";

            _audit.Log(user, role, tenantId, clientId, action, authResult, "Blocked",
                context.Request.Method, statusCode, stopwatch.ElapsedMilliseconds);
        }
    }
}
