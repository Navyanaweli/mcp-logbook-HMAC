namespace McpLogbookApi.Services;

// Typed accessors over HttpContext.Items for the current request's resolved
// HMAC client identity. Set once by HmacAuthenticationMiddleware on success;
// read anywhere downstream (AccessScopeResolver, controllers, tools) instead
// of each caller poking at HttpContext.Items directly with magic strings.
public static class HmacAuthContext
{
    private const string ClientIdKey = "Hmac.ClientId";
    private const string AllowedCompanyIdsKey = "Hmac.AllowedCompanyIds";
    private const string ToolNameKey = "Hmac.ToolName";

    public static void Attach(HttpContext context, Guid clientId, IReadOnlyList<int> allowedCompanyIds, string toolName)
    {
        context.Items[ClientIdKey] = clientId;
        context.Items[AllowedCompanyIdsKey] = allowedCompanyIds;
        context.Items[ToolNameKey] = toolName;
    }

    public static bool IsHmacAuthenticated(HttpContext context) => context.Items.ContainsKey(ClientIdKey);

    public static Guid? GetClientId(HttpContext context) =>
        context.Items.TryGetValue(ClientIdKey, out var value) && value is Guid clientId ? clientId : null;

    public static IReadOnlyList<int> GetAllowedCompanyIds(HttpContext context) =>
        context.Items.TryGetValue(AllowedCompanyIdsKey, out var value) && value is IReadOnlyList<int> companyIds
            ? companyIds
            : [];

    // The tool name the auth middleware already resolved for this request (see
    // HmacAuthenticationMiddleware.ExtractToolNameAsync) -- reused by AccessScopeResolver
    // so the scope-audit entry names the same tool as the authentication-audit entry.
    public static string GetToolName(HttpContext context) =>
        context.Items.TryGetValue(ToolNameKey, out var value) && value is string toolName ? toolName : "unknown";
}
