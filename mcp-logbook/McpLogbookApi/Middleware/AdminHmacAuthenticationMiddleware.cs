using System.Text.Json;
using McpLogbookApi.Services;

namespace McpLogbookApi.Middleware;

// Authenticates the admin surface (/api/admin/* and /api/audit/external) using the same HMAC
// signing contract external clients use (HmacSignatureService) -- just verified against a
// single well-known admin secret (Admin:HmacSecret) instead of a looked-up per-company Client.
// This brings admin-endpoint security in line with the external-client model instead of being
// a weaker, separate static-key mechanism.
//
// There's no "X-Client-Id" here since there's exactly one admin identity -- but unlike
// external clients (each with their own unique secret), every admin request shares the same
// secret. Without something else to distinguish requests, two structurally-identical admin
// requests (same method/path/body) landing in the same whole-second timestamp would produce
// the exact same signature and the second would be falsely rejected as a replay. X-Nonce (a
// client-generated random value, included in the signed content) fixes that: the replay cache
// is keyed on the nonce, so it's the client's job to make each request's nonce unique, exactly
// as it's already the client's job to pick a fresh timestamp.
public class AdminHmacAuthenticationMiddleware
{
    private const string TimestampHeader = "X-Timestamp";
    private const string NonceHeader = "X-Nonce";
    private const string SignatureHeader = "X-Signature";
    private static readonly TimeSpan AllowedClockSkew = TimeSpan.FromMinutes(5);

    private readonly RequestDelegate _next;
    private readonly byte[] _secretBytes;
    private readonly ReplayCache _replayCache = new();

    public AdminHmacAuthenticationMiddleware(RequestDelegate next, IConfiguration config)
    {
        _next = next;
        var secret = config["Admin:HmacSecret"];
        _secretBytes = string.IsNullOrEmpty(secret) ? [] : Convert.FromBase64String(secret);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var isAdminPath = context.Request.Path.StartsWithSegments("/api/admin") ||
            context.Request.Path.StartsWithSegments("/api/audit");

        if (!isAdminPath)
        {
            await _next(context);
            return;
        }

        // Fails closed if Admin:HmacSecret was never configured, rather than treating an
        // empty secret as a valid (and trivially guessable) one
        if (_secretBytes.Length == 0)
        {
            await DenyAsync(context, "Admin:HmacSecret is not configured.");
            return;
        }

        var hasTimestamp = context.Request.Headers.TryGetValue(TimestampHeader, out var timestampValues);
        var hasNonce = context.Request.Headers.TryGetValue(NonceHeader, out var nonceValues);
        var hasSignature = context.Request.Headers.TryGetValue(SignatureHeader, out var signatureValues);

        if (!hasTimestamp || !hasNonce || !hasSignature)
        {
            await DenyAsync(context, "Missing one or more required admin HMAC headers (X-Timestamp, X-Nonce, X-Signature).");
            return;
        }

        var timestampRaw = timestampValues.ToString();
        var nonceRaw = nonceValues.ToString();
        var signatureRaw = signatureValues.ToString();

        if (string.IsNullOrEmpty(nonceRaw))
        {
            await DenyAsync(context, "X-Nonce must not be empty.");
            return;
        }

        // 1. Replay protection FIRST -- reject before ever computing the expected signature
        if (!long.TryParse(timestampRaw, out var timestampUnixSeconds))
        {
            await DenyAsync(context, "X-Timestamp must be a unix timestamp in seconds.");
            return;
        }
        var requestTime = DateTimeOffset.FromUnixTimeSeconds(timestampUnixSeconds);
        if ((DateTimeOffset.UtcNow - requestTime).Duration() > AllowedClockSkew)
        {
            await DenyAsync(context, "Request timestamp is outside the allowed 5-minute window.");
            return;
        }

        // 2. Rebuild the string-to-sign from the actual incoming request
        context.Request.EnableBuffering();
        using var bodyStream = new MemoryStream();
        await context.Request.Body.CopyToAsync(bodyStream);
        context.Request.Body.Position = 0;
        var bodyHash = HmacSignatureService.ComputeBodyHash(bodyStream.ToArray());

        var stringToSign = HmacSignatureService.BuildAdminStringToSign(
            context.Request.Method, context.Request.Path.Value ?? "/", timestampRaw, nonceRaw, bodyHash);
        var expectedSignature = HmacSignatureService.ComputeSignature(_secretBytes, stringToSign);

        // 3. Constant-time comparison -- never use == on signatures
        if (!HmacSignatureService.SignaturesMatch(signatureRaw, expectedSignature))
        {
            await DenyAsync(context, "Signature verification failed.");
            return;
        }

        // 4. Replay protection -- keyed on the nonce (not the signature): it's the client's
        // job to make each request's nonce unique, so two genuinely different requests never
        // collide here even if everything else about them happens to match
        if (!_replayCache.TryRegister(nonceRaw, AllowedClockSkew))
        {
            await DenyAsync(context, "Nonce already used.");
            return;
        }

        await _next(context);
    }

    private static async Task DenyAsync(HttpContext context, string reason)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            error = "unauthorized",
            message = reason
        }));
    }
}
