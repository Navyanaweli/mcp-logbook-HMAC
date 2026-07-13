using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace HmacTestClient;

// Reference implementation of the HMAC signing contract external MCP clients must
// implement -- deliberately standalone (no project reference to McpLogbookApi), since
// a real external client is a separate codebase with no access to the server's code.
// Mirrors McpLogbookApi/Services/HmacSignatureService.cs exactly:
//
//   stringToSign = "{METHOD}\n{PATH}\n{TIMESTAMP}\n{BASE64_SHA256_BODY_HASH}"
//   signature    = Base64(HMAC-SHA256(sharedSecretBytes, stringToSign))
//
// Shared (via a linked file, not a project/assembly reference) between HmacTestClient
// and McpHmacProxy so both demo tools compute signatures identically without either
// duplicating the math by hand.
public static class HmacRequestSigner
{
    public record SignedHeaders(string Timestamp, string Signature);

    public static SignedHeaders Sign(string base64Secret, string method, string path, byte[] bodyBytes)
    {
        long timestamp = GenerateEpochTimestamp();
        // Computed once and reused in both stringToSign and the returned header value --
        // never call .ToString() on the timestamp a second time, so the two can't diverge.
        var timestampString = timestamp.ToString(CultureInfo.InvariantCulture);

        var bodyHash = Convert.ToBase64String(SHA256.HashData(bodyBytes));
        var stringToSign = $"{method.ToUpperInvariant()}\n{path}\n{timestampString}\n{bodyHash}";

        var secretBytes = Convert.FromBase64String(base64Secret);
        var signature = Convert.ToBase64String(HMACSHA256.HashData(secretBytes, Encoding.UTF8.GetBytes(stringToSign)));

        return new SignedHeaders(timestampString, signature);
    }

    private static long GenerateEpochTimestamp()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }
}
