using System.Security.Cryptography;
using System.Text;

namespace McpLogbookApi.Services;

// ── HMAC signing contract for external MCP clients ──────────────────────
//
// External clients must implement matching logic on their end. For every request:
//
//   1. Compute BASE64_SHA256_BODY_HASH = Base64(SHA256(raw request body bytes))
//      (use an empty byte array for requests with no body, e.g. GET)
//   2. Build the string to sign, newline-separated, in this exact order:
//
//        {HTTP_METHOD}\n{REQUEST_PATH}\n{TIMESTAMP}\n{BASE64_SHA256_BODY_HASH}
//
//      - HTTP_METHOD: uppercase (e.g. "GET", "POST")
//      - REQUEST_PATH: path only, no query string, no scheme/host (e.g. "/api/mcp/logbooks")
//      - TIMESTAMP: current unix time in whole seconds, as a string (e.g. "1751980800")
//   3. Compute SIGNATURE = Base64(HMAC-SHA256(sharedSecretBytes, stringToSign))
//   4. Send three headers on the request:
//        X-Client-Id:  <clientId GUID>
//        X-Timestamp:  <the same TIMESTAMP used above>
//        X-Signature:  <the SIGNATURE from step 3>
//
// The server rejects requests whose X-Timestamp is more than 5 minutes from
// server time (replay protection), independent of signature validity.
// See HmacTestClient for a runnable reference implementation.
public static class HmacSignatureService
{
    public static string BuildStringToSign(string httpMethod, string requestPath, string timestamp, string base64BodyHash) =>
        $"{httpMethod.ToUpperInvariant()}\n{requestPath}\n{timestamp}\n{base64BodyHash}";

    // Admin-only variant: includes a client-generated nonce (X-Nonce) in the signed content.
    // External clients don't need this -- each has its own unique per-client secret, so two
    // structurally-identical requests from two different clients (or even the same client)
    // still produce different signatures. The admin identity has exactly one shared secret,
    // so two structurally-identical admin requests (same method/path/body) landing in the
    // same whole-second timestamp would otherwise produce the exact same signature and
    // falsely collide in the replay cache. The nonce breaks that tie.
    public static string BuildAdminStringToSign(string httpMethod, string requestPath, string timestamp, string nonce, string base64BodyHash) =>
        $"{httpMethod.ToUpperInvariant()}\n{requestPath}\n{timestamp}\n{nonce}\n{base64BodyHash}";

    public static string ComputeBodyHash(byte[] bodyBytes) =>
        Convert.ToBase64String(SHA256.HashData(bodyBytes));

    public static string ComputeSignature(byte[] secretBytes, string stringToSign) =>
        Convert.ToBase64String(HMACSHA256.HashData(secretBytes, Encoding.UTF8.GetBytes(stringToSign)));

    // Constant-time comparison of two base64-encoded signatures -- never use == on
    // signatures. Shared by every caller that verifies an HMAC signature against an
    // expected one (external clients and the admin identity alike).
    public static bool SignaturesMatch(string providedBase64, string expectedBase64)
    {
        byte[] provided, expected;
        try
        {
            provided = Convert.FromBase64String(providedBase64);
            expected = Convert.FromBase64String(expectedBase64);
        }
        catch (FormatException)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(provided, expected);
    }
}
