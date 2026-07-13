using System.Security.Cryptography;
using System.Text.Json;
using McpLogbookApi.Models;
using McpLogbookApi.Services;
using Microsoft.AspNetCore.DataProtection;

namespace McpLogbookApi.Middleware;

// Authenticates external MCP clients via HMAC request signing. This is the only
// authentication mechanism for the read-scoped MCP surface: HMAC headers are
// mandatory on /mcp and /api/mcp/* (everything else -- admin endpoints, Swagger --
// passes through untouched; the admin endpoints are gated separately by
// AdminHmacAuthenticationMiddleware, the same signing contract under a single
// well-known admin identity instead of a looked-up Client).
//
// See HmacSignatureService for the exact signing contract external clients
// must implement.
public class HmacAuthenticationMiddleware
{
    private const string ClientIdHeader = "X-Client-Id";
    private const string TimestampHeader = "X-Timestamp";
    private const string SignatureHeader = "X-Signature";
    private static readonly TimeSpan AllowedClockSkew = TimeSpan.FromMinutes(5);

    private readonly RequestDelegate _next;
    private readonly IDataProtector _protector;

    // The middleware instance is constructed once for the app's lifetime, so this cache is
    // effectively a process-wide singleton -- fine for a single-instance deployment, but it
    // wouldn't catch a replay against a different instance behind a load balancer.
    private readonly ReplayCache _replayCache = new();

    public HmacAuthenticationMiddleware(RequestDelegate next, IDataProtectionProvider dataProtectionProvider)
    {
        _next = next;
        _protector = dataProtectionProvider.CreateProtector(ClientOnboardingService.ProtectorPurpose);
    }

    public async Task InvokeAsync(HttpContext context, ExternalClientRepository clientRepo)
    {
        var isProtectedPath = context.Request.Path.StartsWithSegments("/mcp") ||
            context.Request.Path.StartsWithSegments("/api/mcp");

        if (!isProtectedPath)
        {
            await _next(context);
            return;
        }

        var hasClientId = context.Request.Headers.TryGetValue(ClientIdHeader, out var clientIdValues);
        var hasTimestamp = context.Request.Headers.TryGetValue(TimestampHeader, out var timestampValues);
        var hasSignature = context.Request.Headers.TryGetValue(SignatureHeader, out var signatureValues);

        // Buffer now so both the body-hash computation below and downstream MCP/model
        // binding can each read the body from the start independently
        context.Request.EnableBuffering();

        var toolName = await ExtractToolNameAsync(context);

        if (!hasClientId || !hasTimestamp || !hasSignature)
        {
            await DenyAsync(context, clientRepo, null, null, toolName, "Missing one or more required HMAC headers (X-Client-Id, X-Timestamp, X-Signature).");
            return;
        }

        var timestampRaw = timestampValues.ToString();
        var clientIdRaw = clientIdValues.ToString();
        var signatureRaw = signatureValues.ToString();

        // 1. Replay protection FIRST -- reject before ever looking at the client or signature
        if (!long.TryParse(timestampRaw, out var timestampUnixSeconds))
        {
            await DenyAsync(context, clientRepo, null, null, toolName, "X-Timestamp must be a unix timestamp in seconds.");
            return;
        }
        var requestTime = DateTimeOffset.FromUnixTimeSeconds(timestampUnixSeconds);
        if ((DateTimeOffset.UtcNow - requestTime).Duration() > AllowedClockSkew)
        {
            await DenyAsync(context, clientRepo, null, null, toolName, "Request timestamp is outside the allowed 5-minute window.");
            return;
        }

        // 2. Resolve and validate the client
        if (!Guid.TryParse(clientIdRaw, out var clientId))
        {
            await DenyAsync(context, clientRepo, null, null, toolName, "X-Client-Id is not a valid GUID.");
            return;
        }

        var client = clientRepo.GetClientById(clientId);
        if (client is null)
        {
            await DenyAsync(context, clientRepo, clientId, null, toolName, "Unknown client id.");
            return;
        }
        if (client.RevokedAt is not null)
        {
            await DenyAsync(context, clientRepo, clientId, null, toolName, "Client credential has been revoked.");
            return;
        }
        if (client.ExpiresAt is not null && client.ExpiresAt.Value < DateTime.UtcNow)
        {
            await DenyAsync(context, clientRepo, clientId, null, toolName, "Client credential has expired.");
            return;
        }

        // 3. Rebuild the string-to-sign from the actual incoming request
        context.Request.Body.Position = 0;
        using var bodyStream = new MemoryStream();
        await context.Request.Body.CopyToAsync(bodyStream);
        context.Request.Body.Position = 0;
        var bodyHash = HmacSignatureService.ComputeBodyHash(bodyStream.ToArray());

        var stringToSign = HmacSignatureService.BuildStringToSign(
            context.Request.Method, context.Request.Path.Value ?? "/", timestampRaw, bodyHash);

        // 4. Decrypt the stored secret and recompute the expected signature
        byte[] secretBytes;
        try
        {
            secretBytes = Convert.FromBase64String(_protector.Unprotect(client.EncryptedSecret));
        }
        catch (CryptographicException)
        {
            await DenyAsync(context, clientRepo, clientId, null, toolName, "Could not decrypt stored client secret.");
            return;
        }

        var expectedSignature = HmacSignatureService.ComputeSignature(secretBytes, stringToSign);

        // 5. Constant-time comparison -- never use == on signatures
        if (!HmacSignatureService.SignaturesMatch(signatureRaw, expectedSignature))
        {
            await DenyAsync(context, clientRepo, clientId, null, toolName, "Signature verification failed.");
            return;
        }

        // 6. Replay protection -- a signature can only ever be used once
        if (!_replayCache.TryRegister(signatureRaw, AllowedClockSkew))
        {
            await DenyAsync(context, clientRepo, clientId, null, toolName, "Signature already used.");
            return;
        }

        // Success: resolve allowed companies, attach to HttpContext.Items, record usage
        var allowedCompanyIds = clientRepo.GetAllowedCompanyIds(clientId);
        HmacAuthContext.Attach(context, clientId, allowedCompanyIds, toolName);
        clientRepo.UpdateLastUsed(clientId, DateTime.UtcNow);
        clientRepo.LogAttempt(clientId, null, toolName, allowed: true, denialReason: null);

        await _next(context);
    }

    // Best-effort tool name for the audit log: for MCP JSON-RPC calls (POST /mcp,
    // method "tools/call") this is the actual tool name from the request body;
    // for everything else (e.g. the REST endpoints under /api/mcp) it's "METHOD path".
    private static async Task<string> ExtractToolNameAsync(HttpContext context)
    {
        var fallback = $"{context.Request.Method} {context.Request.Path}";

        if (context.Request.ContentLength is null or 0)
            return fallback;

        try
        {
            context.Request.Body.Position = 0;
            using var document = await JsonDocument.ParseAsync(context.Request.Body);
            context.Request.Body.Position = 0;

            var root = document.RootElement;
            if (root.TryGetProperty("method", out var methodProp) &&
                methodProp.GetString() == "tools/call" &&
                root.TryGetProperty("params", out var paramsProp) &&
                paramsProp.TryGetProperty("name", out var nameProp))
            {
                return nameProp.GetString() ?? fallback;
            }
        }
        catch (JsonException)
        {
            context.Request.Body.Position = 0;
        }

        return fallback;
    }

    private static async Task DenyAsync(
        HttpContext context, ExternalClientRepository clientRepo, Guid? clientId, int? requestedCompanyId,
        string toolName, string reason)
    {
        clientRepo.LogAttempt(clientId, requestedCompanyId, toolName, allowed: false, denialReason: reason);

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            error = "unauthorized",
            message = reason
        }));
    }
}
