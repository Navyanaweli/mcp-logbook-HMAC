namespace McpLogbookApi.Models;

// Written once by `dotnet run -- --seed-demo-client` (see Program.cs) and read by
// McpHmacProxy on every startup. DEMO/LOCAL TESTING ONLY -- the file this is
// serialized to (Data/demo-client-credentials.json) is gitignored since it contains
// a raw shared secret in plaintext.
public record DemoClientCredentials(Guid ClientId, string SharedSecret, int CompanyId, DateTime CreatedAt);
