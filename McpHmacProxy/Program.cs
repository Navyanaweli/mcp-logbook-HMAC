using System.Text;
using System.Text.Json;
using HmacTestClient;

// Local HMAC-signing proxy: sits between Claude Desktop (which speaks MCP over stdio,
// the only transport it supports for locally-run servers) and McpLogbookApi's real
// MCP endpoint (which speaks MCP over streamable-HTTP, protected by
// HmacAuthenticationMiddleware). For every JSON-RPC message Claude Desktop writes to
// stdin, this signs it per the contract in HmacRequestSigner.cs / the server's
// HmacSignatureService.cs and forwards it as an HTTP POST.
//
// The ModelContextProtocol.AspNetCore SDK requires the client to accept BOTH
// application/json and text/event-stream (a bare "application/json" Accept header is
// rejected with 406), and even in Stateless mode it always answers with a single SSE
// event ("event: message\ndata: {...}\n\n"), never a bare JSON body. So each response is
// unwrapped back to the plain JSON-RPC payload from its "data:" line(s) before being
// written to stdout -- everything in that payload (including tool-level
// { "error": "access_denied" } results) is passed through exactly as the server
// produced it, so the LLM sees the real result and can relay it naturally.
//
// DEMO / LOCAL TESTING ONLY -- a single hardcoded client credential, no reconnect logic.
//
// Usage:
//   McpHmacProxy --credentials <path-to-demo-client-credentials.json> [--base-url <url>]
//   (or positionally: McpHmacProxy <credentialsPath> [baseUrl])
//
// All diagnostic output goes to stderr -- stdout is reserved exclusively for MCP
// JSON-RPC response messages, per the stdio transport contract.

// Forces UTF-8 without a BOM on both stdio streams. Without this, a redirected stdout/stdin
// on Windows can fall back to the OEM code page (breaking non-ASCII payload content, e.g.
// ship log text like "08°30N") or emit a stray BOM as the first bytes written, which some
// JSON-RPC stdio clients choke on. Must happen before any Console.In/Console.Out use.
var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
Console.OutputEncoding = utf8NoBom;
Console.InputEncoding = utf8NoBom;

string? credentialsPath = null;
string baseUrl = "http://localhost:5044";
const string mcpPath = "/mcp";

var positional = new List<string>();
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--credentials" when i + 1 < args.Length:
            credentialsPath = args[++i];
            break;
        case "--base-url" when i + 1 < args.Length:
            baseUrl = args[++i];
            break;
        default:
            positional.Add(args[i]);
            break;
    }
}
credentialsPath ??= positional.ElementAtOrDefault(0);
if (positional.Count > 1)
    baseUrl = positional[1];

if (string.IsNullOrWhiteSpace(credentialsPath))
{
    await Console.Error.WriteLineAsync(
        "Usage: McpHmacProxy --credentials <path-to-demo-client-credentials.json> [--base-url <url>]");
    return 1;
}

if (!File.Exists(credentialsPath))
{
    await Console.Error.WriteLineAsync($"Credentials file not found: {credentialsPath}");
    await Console.Error.WriteLineAsync(
        "Generate one by running, from the McpLogbookApi project: dotnet run -- --seed-demo-client");
    return 1;
}

DemoCredentials credentials;
try
{
    credentials = JsonSerializer.Deserialize<DemoCredentials>(
        await File.ReadAllTextAsync(credentialsPath),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new InvalidDataException("Credentials file deserialized to null.");
}
catch (Exception ex)
{
    await Console.Error.WriteLineAsync($"Failed to read credentials from {credentialsPath}: {ex.Message}");
    return 1;
}

await Console.Error.WriteLineAsync($"McpHmacProxy: forwarding to {baseUrl}{mcpPath} as client {credentials.ClientId}");

using var httpClient = new HttpClient { BaseAddress = new Uri(baseUrl) };

string? line;
while ((line = await Console.In.ReadLineAsync()) is not null)
{
    if (line.Length == 0)
        continue;

    await Console.Error.WriteLineAsync($"--> {Truncate(line)}");

    try
    {
        var payloads = await ForwardAsync(httpClient, mcpPath, credentials, line);
        foreach (var payload in payloads)
        {
            // Belt-and-suspenders: never let a blank line reach stdout -- Claude Desktop
            // parses every stdout line as a JSON-RPC message and fails on an empty one.
            if (string.IsNullOrWhiteSpace(payload))
                continue;

            await Console.Error.WriteLineAsync($"<-- {Truncate(payload)}");
            Console.Out.Write(payload);
            Console.Out.Write('\n');
        }
        await Console.Out.FlushAsync();
    }
    catch (Exception ex)
    {
        await Console.Error.WriteLineAsync($"Request failed: {ex}");
        var errorResponse = BuildJsonRpcError(line, ex.Message);
        Console.Out.Write(errorResponse);
        Console.Out.Write('\n');
        await Console.Out.FlushAsync();
    }
}

return 0;

static async Task<IReadOnlyList<string>> ForwardAsync(HttpClient httpClient, string path, DemoCredentials credentials, string jsonRpcLine)
{
    var bodyBytes = Encoding.UTF8.GetBytes(jsonRpcLine);
    var signed = HmacRequestSigner.Sign(credentials.SharedSecret, "POST", path, bodyBytes);

    using var request = new HttpRequestMessage(HttpMethod.Post, path)
    {
        Content = new ByteArrayContent(bodyBytes)
    };
    request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
    // Both are required -- the server's MCP SDK returns 406 if text/event-stream is absent
    request.Headers.Add("Accept", "application/json, text/event-stream");
    request.Headers.Add("X-Client-Id", credentials.ClientId.ToString());
    request.Headers.Add("X-Timestamp", signed.Timestamp);
    request.Headers.Add("X-Signature", signed.Signature);

    using var response = await httpClient.SendAsync(request);
    var body = await response.Content.ReadAsStringAsync();

    var isSse = response.Content.Headers.ContentType?.MediaType == "text/event-stream";
    if (!isSse)
        // No response body at all for e.g. a JSON-RPC notification (no "id", no reply
        // expected) -- nothing to write to stdout for those.
        return string.IsNullOrWhiteSpace(body) ? [] : [body];

    return ExtractSseDataPayloads(body);
}

// Unwraps one or more SSE "event: message\ndata: <json>\n\n" blocks back into their
// plain JSON-RPC payloads. Multi-line "data:" fields within a single event are
// rejoined with '\n' per the SSE spec, though in practice each payload here is one line.
// Keep-alive/ping events (an empty "data:" line, or a comment-only block with no "data:"
// line at all) must never surface as a blank stdout write -- Claude Desktop treats every
// stdout line as a JSON-RPC message and fails to parse an empty one.
static IReadOnlyList<string> ExtractSseDataPayloads(string sseText)
{
    var payloads = new List<string>();
    var currentDataLines = new List<string>();

    void FlushEvent()
    {
        var joined = string.Join('\n', currentDataLines);
        if (!string.IsNullOrWhiteSpace(joined))
            payloads.Add(joined);
        currentDataLines.Clear();
    }

    foreach (var rawLine in sseText.Split('\n'))
    {
        var line = rawLine.TrimEnd('\r');
        if (line.Length == 0)
        {
            FlushEvent();
            continue;
        }
        if (line.StartsWith("data:", StringComparison.Ordinal))
        {
            var value = line["data:".Length..];
            if (value.StartsWith(' '))
                value = value[1..];
            currentDataLines.Add(value);
        }
        // "event:", "id:", "retry:", and comment (":") lines carry no JSON-RPC payload
    }
    FlushEvent();

    return payloads;
}

// Best-effort JSON-RPC 2.0 error envelope for proxy-level failures (e.g. the API is
// unreachable) so Claude Desktop gets a real response instead of hanging. Tool-level
// errors from the server (including access_denied) never reach this path -- they
// already come back as ordinary response bodies from ForwardAsync above.
static string BuildJsonRpcError(string requestLine, string message)
{
    object? id = null;
    try
    {
        using var document = JsonDocument.Parse(requestLine);
        if (document.RootElement.TryGetProperty("id", out var idProp))
            id = idProp.ValueKind switch
            {
                JsonValueKind.Number => idProp.GetInt64(),
                JsonValueKind.String => idProp.GetString(),
                _ => null
            };
    }
    catch (JsonException)
    {
        // Malformed request line -- fall through with id = null
    }

    return JsonSerializer.Serialize(new
    {
        jsonrpc = "2.0",
        id,
        error = new { code = -32000, message = $"McpHmacProxy: {message}" }
    });
}

static string Truncate(string value, int max = 300) =>
    value.Length <= max ? value : value[..max] + "...";

record DemoCredentials(Guid ClientId, string SharedSecret, int CompanyId, DateTime CreatedAt);
