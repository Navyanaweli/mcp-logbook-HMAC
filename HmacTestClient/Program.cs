using System.Text;
using HmacTestClient;

// Standalone reference implementation of the HMAC signing contract external MCP
// clients must implement -- deliberately has NO project reference to McpLogbookApi,
// since a real external client is a separate codebase with no access to that code.
// Signing math itself lives in HmacRequestSigner.cs (shared with McpHmacProxy via a
// linked file, not a project reference).
//
// Usage:
//   dotnet run -- <clientId> <base64Secret> [baseUrl] [method] [path] [body]
//
// Example, after onboarding a client via POST /api/admin/clients:
//   dotnet run -- 3fa85f64-5717-4562-b3fc-2c963f66afa6 Yk3f9...== http://localhost:5044 GET /api/mcp/logbooks

if (args.Length < 2)
{
    Console.WriteLine("Usage: dotnet run -- <clientId> <base64Secret> [baseUrl] [method] [path] [body]");
    return 1;
}

var clientId = args[0];
var base64Secret = args[1];
var baseUrl = args.Length > 2 ? args[2] : "http://localhost:5044";
var method = args.Length > 3 ? args[3] : "GET";
var path = args.Length > 4 ? args[4] : "/api/mcp/logbooks";
var body = args.Length > 5 ? args[5] : "";

var bodyBytes = Encoding.UTF8.GetBytes(body);
var signed = HmacRequestSigner.Sign(base64Secret, method, path, bodyBytes);

Console.WriteLine("Headers to send:");
Console.WriteLine($"  X-Client-Id: {clientId}");
Console.WriteLine($"  X-Timestamp: {signed.Timestamp}");
Console.WriteLine($"  X-Signature: {signed.Signature}");
Console.WriteLine();

using var httpClient = new HttpClient { BaseAddress = new Uri(baseUrl) };
using var request = new HttpRequestMessage(new HttpMethod(method), path);
request.Headers.Add("X-Client-Id", clientId);
request.Headers.Add("X-Timestamp", signed.Timestamp);
request.Headers.Add("X-Signature", signed.Signature);
if (bodyBytes.Length > 0)
    request.Content = new ByteArrayContent(bodyBytes);

Console.WriteLine($"Sending {method} {baseUrl}{path} ...");
var response = await httpClient.SendAsync(request);
var responseBody = await response.Content.ReadAsStringAsync();

Console.WriteLine($"Response: {(int)response.StatusCode} {response.StatusCode}");
Console.WriteLine(responseBody);

return 0;
