using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace McpLogbookApi.Tests;

// End-to-end coverage for the HMAC path: onboarding via the admin endpoint (itself gated by
// AdminHmacAuthenticationMiddleware, the same signing contract under a single well-known
// admin identity), then authenticating an external client purely via
// X-Client-Id/X-Timestamp/X-Signature, scoped by company via Ships.CompanyId.
//
// Seed data: Company 1 (Ocean Star Shipping Co.) -> MV OCEAN STAR + MV NORTHERN LIGHT
//            Company 2 (Pacific Container Lines) -> MV PACIFIC DAWN
public class ExternalClientAuthenticationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ExternalClientAuthenticationTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    private async Task<(Guid ClientId, string Secret)> OnboardClientAsync(int primaryCompanyId, DateTime? expiresAt = null)
    {
        var bodyBytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            clientName = "Test External Client",
            primaryCompanyId,
            expiresAt
        });

        var response = await _client.SendAsync(BuildAdminRequest(HttpMethod.Post, "/api/admin/clients", bodyBytes: bodyBytes));
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (Guid.Parse(body.GetProperty("clientId").GetString()!), body.GetProperty("sharedSecret").GetString()!);
    }

    private static (string Timestamp, string Signature) Sign(string secret, string method, string path, string body)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var bodyHash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(body)));
        var stringToSign = $"{method}\n{path}\n{timestamp}\n{bodyHash}";
        var signature = Convert.ToBase64String(
            HMACSHA256.HashData(Convert.FromBase64String(secret), Encoding.UTF8.GetBytes(stringToSign)));
        return (timestamp, signature);
    }

    private static (string Timestamp, string Signature) Sign(string secret, string method, string path, byte[] bodyBytes)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var bodyHash = Convert.ToBase64String(SHA256.HashData(bodyBytes));
        var stringToSign = $"{method}\n{path}\n{timestamp}\n{bodyHash}";
        var signature = Convert.ToBase64String(
            HMACSHA256.HashData(Convert.FromBase64String(secret), Encoding.UTF8.GetBytes(stringToSign)));
        return (timestamp, signature);
    }

    private HttpRequestMessage BuildSignedRequest(Guid clientId, string secret, HttpMethod method, string path)
    {
        var (timestamp, signature) = Sign(secret, method.Method, path, "");
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Client-Id", clientId.ToString());
        request.Headers.Add("X-Timestamp", timestamp);
        request.Headers.Add("X-Signature", signature);
        return request;
    }

    // Builds a signed POST to the real MCP JSON-RPC endpoint (as opposed to the plain REST
    // endpoints under /api/mcp) -- needed to exercise LogbookTools (and therefore
    // AccessScopeResolver's scope-audit logging) rather than McpController.
    private HttpRequestMessage BuildMcpRequest(Guid clientId, string secret, string jsonRpcBody)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(jsonRpcBody);
        var (timestamp, signature) = Sign(secret, "POST", "/mcp", bodyBytes);
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new ByteArrayContent(bodyBytes)
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        // The MCP SDK rejects a bare "application/json" Accept header with 406
        request.Headers.Add("Accept", "application/json, text/event-stream");
        request.Headers.Add("X-Client-Id", clientId.ToString());
        request.Headers.Add("X-Timestamp", timestamp);
        request.Headers.Add("X-Signature", signature);
        return request;
    }

    // Builds a request signed for the admin identity (no X-Client-Id -- there's only one
    // admin). signaturePath is what gets signed (path only, no query string, matching what
    // AdminHmacAuthenticationMiddleware rebuilds server-side); requestUri is what's actually
    // sent, letting a query string be attached without it being part of the signed string.
    //
    // A fresh X-Nonce is generated per call unless nonceOverride is given: since every admin
    // request shares the one admin secret (unlike external clients, each with their own),
    // two structurally-identical admin requests would otherwise produce the exact same
    // signature and collide in the replay cache. The nonce is what makes them distinguishable
    // -- see AdminHmacAuthenticationMiddleware and HmacSignatureService.BuildAdminStringToSign.
    private HttpRequestMessage BuildAdminRequest(
        HttpMethod method, string signaturePath, string? requestUri = null, byte[]? bodyBytes = null,
        string? secretOverride = null, string? nonceOverride = null, string? timestampOverride = null)
    {
        bodyBytes ??= [];
        var secret = secretOverride ?? CustomWebApplicationFactory.TestAdminHmacSecret;
        var timestamp = timestampOverride ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var nonce = nonceOverride ?? Guid.NewGuid().ToString();
        var bodyHash = Convert.ToBase64String(SHA256.HashData(bodyBytes));
        var stringToSign = $"{method.Method}\n{signaturePath}\n{timestamp}\n{nonce}\n{bodyHash}";
        var signature = Convert.ToBase64String(
            HMACSHA256.HashData(Convert.FromBase64String(secret), Encoding.UTF8.GetBytes(stringToSign)));

        var request = new HttpRequestMessage(method, requestUri ?? signaturePath);
        if (bodyBytes.Length > 0)
        {
            request.Content = new ByteArrayContent(bodyBytes);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }
        request.Headers.Add("X-Timestamp", timestamp);
        request.Headers.Add("X-Nonce", nonce);
        request.Headers.Add("X-Signature", signature);
        return request;
    }

    // The MCP endpoint always answers with a single SSE "data: {...}" event, never a bare
    // JSON body -- unwraps it back to the plain JSON-RPC payload.
    private static string UnwrapSseData(string sseText)
    {
        foreach (var rawLine in sseText.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.StartsWith("data:", StringComparison.Ordinal))
                return line["data:".Length..].TrimStart(' ');
        }
        return sseText;
    }

    [Fact]
    public async Task ValidSignature_ReturnsCompanyScopedLogbooks()
    {
        var (clientId, secret) = await OnboardClientAsync(primaryCompanyId: 1);

        var response = await _client.SendAsync(BuildSignedRequest(clientId, secret, HttpMethod.Get, "/api/mcp/logbooks"));
        var responseBody = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("MV OCEAN STAR", responseBody);
        Assert.Contains("MV NORTHERN LIGHT", responseBody);
        Assert.DoesNotContain("MV PACIFIC DAWN", responseBody);
    }

    [Fact]
    public async Task ValidSignature_DifferentCompany_ReturnsOnlyItsOwnShips()
    {
        var (clientId, secret) = await OnboardClientAsync(primaryCompanyId: 2);

        var response = await _client.SendAsync(BuildSignedRequest(clientId, secret, HttpMethod.Get, "/api/mcp/logbooks"));
        var responseBody = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("MV PACIFIC DAWN", responseBody);
        Assert.DoesNotContain("MV OCEAN STAR", responseBody);
    }

    [Fact]
    public async Task TamperedSignature_Returns401()
    {
        var (clientId, secret) = await OnboardClientAsync(primaryCompanyId: 1);
        var request = BuildSignedRequest(clientId, secret, HttpMethod.Get, "/api/mcp/logbooks");
        request.Headers.Remove("X-Signature");
        request.Headers.Add("X-Signature", Convert.ToBase64String(new byte[32]));

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // Proves HmacAuthenticationMiddleware's replay cache: the exact same signed request
    // (identical timestamp + signature) is accepted the first time and rejected the second,
    // even though nothing about the request itself changed.
    [Fact]
    public async Task ReplayedSignature_SecondIdenticalRequestReturns401()
    {
        var (clientId, secret) = await OnboardClientAsync(primaryCompanyId: 1);
        var (timestamp, signature) = Sign(secret, "GET", "/api/mcp/logbooks", "");

        HttpRequestMessage BuildRequestWithFixedSignature()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/mcp/logbooks");
            request.Headers.Add("X-Client-Id", clientId.ToString());
            request.Headers.Add("X-Timestamp", timestamp);
            request.Headers.Add("X-Signature", signature);
            return request;
        }

        var firstResponse = await _client.SendAsync(BuildRequestWithFixedSignature());
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        var secondResponse = await _client.SendAsync(BuildRequestWithFixedSignature());
        Assert.Equal(HttpStatusCode.Unauthorized, secondResponse.StatusCode);

        var secondBody = await secondResponse.Content.ReadAsStringAsync();
        Assert.Contains("already used", secondBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StaleTimestamp_Returns401_RegardlessOfSignatureValidity()
    {
        var (clientId, secret) = await OnboardClientAsync(primaryCompanyId: 1);

        var staleTimestamp = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds().ToString();
        var bodyHash = Convert.ToBase64String(SHA256.HashData([]));
        var stringToSign = $"GET\n/api/mcp/logbooks\n{staleTimestamp}\n{bodyHash}";
        var signature = Convert.ToBase64String(
            HMACSHA256.HashData(Convert.FromBase64String(secret), Encoding.UTF8.GetBytes(stringToSign)));

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/mcp/logbooks");
        request.Headers.Add("X-Client-Id", clientId.ToString());
        request.Headers.Add("X-Timestamp", staleTimestamp);
        request.Headers.Add("X-Signature", signature);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RevokedClient_Returns401()
    {
        var (clientId, secret) = await OnboardClientAsync(primaryCompanyId: 1, expiresAt: DateTime.UtcNow.AddDays(-1));

        var response = await _client.SendAsync(BuildSignedRequest(clientId, secret, HttpMethod.Get, "/api/mcp/logbooks"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // Proves PATCH /api/admin/clients/{clientId}/revoke takes effect immediately: a client
    // that could read logbooks a moment ago gets 401 on its very next request afterward.
    [Fact]
    public async Task RevokeEndpoint_ClientGets401OnItsNextRequest()
    {
        var (clientId, secret) = await OnboardClientAsync(primaryCompanyId: 1);

        var beforeRevoke = await _client.SendAsync(BuildSignedRequest(clientId, secret, HttpMethod.Get, "/api/mcp/logbooks"));
        Assert.Equal(HttpStatusCode.OK, beforeRevoke.StatusCode);

        var revokeResponse = await _client.SendAsync(BuildAdminRequest(HttpMethod.Patch, $"/api/admin/clients/{clientId}/revoke"));
        Assert.Equal(HttpStatusCode.OK, revokeResponse.StatusCode);

        var afterRevoke = await _client.SendAsync(BuildSignedRequest(clientId, secret, HttpMethod.Get, "/api/mcp/logbooks"));
        Assert.Equal(HttpStatusCode.Unauthorized, afterRevoke.StatusCode);
    }

    [Fact]
    public async Task RevokeEndpoint_UnknownClientId_Returns404()
    {
        var response = await _client.SendAsync(BuildAdminRequest(HttpMethod.Patch, $"/api/admin/clients/{Guid.NewGuid()}/revoke"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RevokeEndpoint_MissingAdminSignature_Returns401()
    {
        var response = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, $"/api/admin/clients/{Guid.NewGuid()}/revoke"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MissingAdminSignature_CannotOnboardClients()
    {
        var bodyBytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            clientName = "Should Not Be Created",
            primaryCompanyId = 1,
            expiresAt = (DateTime?)null
        });
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/clients")
        {
            Content = new ByteArrayContent(bodyBytes)
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WrongAdminSecret_CannotOnboardClients()
    {
        var bodyBytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            clientName = "Should Not Be Created",
            primaryCompanyId = 1,
            expiresAt = (DateTime?)null
        });

        // Valid base64, deliberately not the secret configured via Admin:HmacSecret
        const string wrongSecret = "d3Jvbmctc2VjcmV0LWZvci10ZXN0aW5nLXB1cnBvc2Vz";
        var response = await _client.SendAsync(
            BuildAdminRequest(HttpMethod.Post, "/api/admin/clients", bodyBytes: bodyBytes, secretOverride: wrongSecret));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // Proves the X-Nonce fix: two structurally-identical admin requests -- same method, path,
    // body, and even the exact same timestamp -- succeed independently as long as each has
    // its own nonce. Before this fix, the replay cache was keyed on the signature alone, and
    // since the admin identity has one shared secret, this exact scenario (same everything
    // except nonce) would have produced an identical signature and falsely rejected the
    // second request as a replay.
    [Fact]
    public async Task TwoIdenticalAdminRequests_WithDifferentNonces_BothSucceed()
    {
        var sameTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

        var firstResponse = await _client.SendAsync(
            BuildAdminRequest(HttpMethod.Get, "/api/audit/external", timestampOverride: sameTimestamp));
        var secondResponse = await _client.SendAsync(
            BuildAdminRequest(HttpMethod.Get, "/api/audit/external", timestampOverride: sameTimestamp));

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
    }

    // The flip side of the fix above: reusing the same nonce twice is still correctly
    // rejected, regardless of timestamp -- replay protection is keyed on the nonce itself,
    // not incidentally on the signature.
    [Fact]
    public async Task ReusedAdminNonce_SecondRequestReturns401()
    {
        const string fixedNonce = "fixed-test-nonce-for-replay-check";

        var firstResponse = await _client.SendAsync(
            BuildAdminRequest(HttpMethod.Get, "/api/audit/external", nonceOverride: fixedNonce));
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        var secondResponse = await _client.SendAsync(
            BuildAdminRequest(HttpMethod.Get, "/api/audit/external", nonceOverride: fixedNonce));
        Assert.Equal(HttpStatusCode.Unauthorized, secondResponse.StatusCode);

        var secondBody = await secondResponse.Content.ReadAsStringAsync();
        Assert.Contains("nonce already used", secondBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExternalAuditLog_OnlyAdminCanQuery_AndRecordsAttempts()
    {
        var (clientId, secret) = await OnboardClientAsync(primaryCompanyId: 1);
        await _client.SendAsync(BuildSignedRequest(clientId, secret, HttpMethod.Get, "/api/mcp/logbooks"));

        var response = await _client.SendAsync(
            BuildAdminRequest(HttpMethod.Get, "/api/audit/external", requestUri: $"/api/audit/external?clientId={clientId}"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(clientId.ToString(), body);
    }

    // Proves the scope-audit entry added in AccessScopeResolver.GetAccessibleShipIds never
    // names a company/ship this client isn't authorized for -- not just that it works once
    // when tested manually. Company 1 owns ships 1 (Ocean Star) and 2 (Northern Light) only;
    // ship 3 (Pacific Dawn) belongs to Company 2 and must never appear.
    [Fact]
    public async Task ScopeAuditLog_RecordsOnlyTheCompaniesAndShipsTheClientIsAuthorizedFor()
    {
        var (clientId, secret) = await OnboardClientAsync(primaryCompanyId: 1);

        const string toolCallBody =
            """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"get_logbook_entries","arguments":{}}}""";
        var toolResponse = await _client.SendAsync(BuildMcpRequest(clientId, secret, toolCallBody));
        Assert.Equal(HttpStatusCode.OK, toolResponse.StatusCode);

        var toolPayload = UnwrapSseData(await toolResponse.Content.ReadAsStringAsync());
        using var toolDoc = JsonDocument.Parse(toolPayload);
        Assert.False(toolDoc.RootElement.TryGetProperty("error", out _));

        var auditResponse = await _client.SendAsync(
            BuildAdminRequest(HttpMethod.Get, "/api/audit/external", requestUri: $"/api/audit/external?clientId={clientId}"));
        Assert.Equal(HttpStatusCode.OK, auditResponse.StatusCode);

        using var auditDoc = JsonDocument.Parse(await auditResponse.Content.ReadAsStringAsync());
        var rowsForThisCall = auditDoc.RootElement.EnumerateArray()
            .Where(row => row.GetProperty("toolName").GetString() == "get_logbook_entries")
            .ToList();

        // 1. Exactly two rows for this call: the auth-level entry (middleware) and the
        // scope-level entry (AccessScopeResolver), matched by ToolName
        Assert.Equal(2, rowsForThisCall.Count);

        // 2. The existing auth-level entry is untouched: allowed, no denial reason
        var authRow = Assert.Single(rowsForThisCall, row => row.GetProperty("denialReason").ValueKind == JsonValueKind.Null);
        Assert.True(authRow.GetProperty("allowed").GetBoolean());

        // 3. The new scope-level entry names company 1 and exactly ships 1+2 -- never
        // ship 3, which belongs to the other company
        var scopeRow = Assert.Single(rowsForThisCall, row => row.GetProperty("denialReason").ValueKind != JsonValueKind.Null);
        Assert.True(scopeRow.GetProperty("allowed").GetBoolean());

        var denialReason = scopeRow.GetProperty("denialReason").GetString()!;
        Assert.Contains("Scoped to companies: [1]", denialReason);

        var shipsSection = denialReason[(denialReason.IndexOf("ships: [", StringComparison.Ordinal) + "ships: [".Length)..];
        var shipIds = shipsSection.TrimEnd(']').Split(", ").Select(int.Parse).ToList();
        Assert.Equal([1, 2], shipIds.OrderBy(id => id));
        Assert.DoesNotContain(3, shipIds);
    }
}
