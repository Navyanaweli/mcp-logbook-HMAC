namespace McpLogbookApi.Services;

// Centralized read-scope enforcement so individual MCP tools / controller actions never
// have to resolve ship access themselves: external HMAC clients are scoped via
// Ships.CompanyId, resolved through HmacAuthContext's AllowedCompanyIds.
public class AccessScopeResolver
{
    private readonly LogbookRepository _logbookRepo;
    private readonly ExternalClientRepository _clientRepo;

    public AccessScopeResolver(LogbookRepository logbookRepo, ExternalClientRepository clientRepo)
    {
        _logbookRepo = logbookRepo;
        _clientRepo = clientRepo;
    }

    // The single call site every MCP tool / controller action should use to resolve
    // which ship IDs the current HMAC-authenticated caller may read. HmacAuthenticationMiddleware
    // guarantees every request reaching this point is HMAC-authenticated.
    public IReadOnlyList<int> GetAccessibleShipIds(HttpContext context)
    {
        var allowedCompanyIds = HmacAuthContext.GetAllowedCompanyIds(context);
        var shipIds = _logbookRepo.GetShipIdsForCompanies(allowedCompanyIds);

        // The auth middleware already logged that this client was allowed to make the
        // request; this second entry makes the resulting *data scope* visible too, since
        // otherwise the audit log can never prove which companies/ships a request was
        // actually restricted to.
        _clientRepo.LogAttempt(
            HmacAuthContext.GetClientId(context),
            requestedCompanyId: null,
            HmacAuthContext.GetToolName(context),
            allowed: true,
            denialReason: $"Scoped to companies: [{string.Join(", ", allowedCompanyIds)}], ships: [{string.Join(", ", shipIds)}]");

        return shipIds;
    }

    // Hook point for any tool/endpoint that accepts an explicit CompanyId parameter (none
    // currently do). Returns null when access is allowed -- either the caller isn't an HMAC
    // client (this check only applies to external clients) or the requested company is one
    // they're allowed to see. Otherwise logs the denial and returns the structured denial
    // response an MCP tool can return directly as its result for the LLM to relay.
    public object? CheckExplicitCompanyAccess(HttpContext context, int requestedCompanyId, string toolName)
    {
        if (!HmacAuthContext.IsHmacAuthenticated(context))
            return null;

        var allowedCompanyIds = HmacAuthContext.GetAllowedCompanyIds(context);
        if (allowedCompanyIds.Contains(requestedCompanyId))
            return null;

        var clientId = HmacAuthContext.GetClientId(context);
        _clientRepo.LogAttempt(clientId, requestedCompanyId, toolName, allowed: false,
            denialReason: "Requested company not in client's allowed companies.");

        return new { error = "access_denied", message = "You do not have access to this company" };
    }
}
