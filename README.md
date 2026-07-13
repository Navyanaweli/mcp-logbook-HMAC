# MCP Logbook

A ship logbook API for external, machine-to-machine clients. Callers authenticate with an
HMAC-signed request (no interactive login, no OAuth), and every request is scoped to the
ships their company is allowed to see. The same data is exposed two ways: as plain REST
endpoints, and as MCP tools an AI agent (e.g. Claude Desktop) can call directly.

This project previously also had a Microsoft Entra ID (Azure AD) OAuth path with four
internal RBAC roles, for internal per-user access. That path has been fully removed — this
is now a single-purpose, external-HMAC-client-only API. See "History" at the bottom if
you're looking for that code.

## Architecture

- **ASP.NET Core 8** Web API (`mcp-logbook/McpLogbookApi/`)
- **Raw ADO.NET + SQLite** (`Microsoft.Data.Sqlite`) — no ORM; every query is hand-written SQL
- **MCP server** via `ModelContextProtocol.AspNetCore`, exposing the logbook data as callable
  tools (`Tools/LogbookTools.cs`) over JSON-RPC at `POST /mcp`
- **HMAC request signing** as the only authentication mechanism, for both external clients
  (`HmacAuthenticationMiddleware`) and the admin surface (`AdminHmacAuthenticationMiddleware`)
  — the same signing contract (`HmacSignatureService`) throughout, just verified against
  different secrets

## Authentication & company-scoping model

There is exactly one way to authenticate anywhere in this API: sign the request with an HMAC
shared secret. External clients sign with the secret issued when they were onboarded; the
admin surface signs with the one well-known admin secret (see "Admin endpoints" below) — same
contract, different secret.

### Signing a request

For every request, the client computes:

```
BASE64_SHA256_BODY_HASH = Base64(SHA256(raw request body bytes))   -- empty byte array for GET
stringToSign             = "{METHOD}\n{PATH}\n{TIMESTAMP}\n{BASE64_SHA256_BODY_HASH}"
                            -- METHOD uppercase, PATH is path-only (no query string/host)
                            -- TIMESTAMP is unix time in whole seconds, as a string
signature                 = Base64(HMAC-SHA256(sharedSecretBytes, stringToSign))
```

...and sends three headers on every request:

| Header | Value |
|---|---|
| `X-Client-Id` | the client's GUID, issued at onboarding |
| `X-Timestamp` | the same `TIMESTAMP` used in `stringToSign` |
| `X-Signature` | the `signature` computed above |

`HmacAuthenticationMiddleware` verifies these on every request under `/mcp` and `/api/mcp/*`
(these two path prefixes are the only ones it protects — everything else, like the admin
endpoints and Swagger, is gated separately or left open):

1. Parses `X-Timestamp` as a `long` unix timestamp and rejects the request if it's more than
   5 minutes from server time (replay protection), *before* even looking at the client or
   signature.
2. Resolves the client by `X-Client-Id` and rejects unknown, revoked, or expired clients.
3. Rebuilds `stringToSign` from the *actual* incoming request (method, path, timestamp, body
   hash) and recomputes the expected signature from the client's decrypted stored secret.
4. Compares the provided and expected signatures with `CryptographicOperations.FixedTimeEquals`
   (constant-time, never `==`).
5. Registers the signature in an in-memory replay cache and rejects the request if it's
   already been seen — the exact same signed request can only ever succeed once.

A request missing any of the three headers, or failing any of the checks above, gets a 401
with a `{"error": "unauthorized", "message": "..."}` body, and every attempt — allowed or
denied — is written to `ExternalClientAuditLog`.

### Company scoping

Access is scoped by **company**, not by individual client:

```
Client --(ClientCompanyAccess)--> Company --(Ships.CompanyId)--> Ship
```

- A `Client` has a primary `CompanyId` and, via `ClientCompanyAccess`, may be granted access to
  more than one company.
- A `Company` sees whichever ships it owns via `Ships.CompanyId` — this is **one-to-many**: a
  company can own many ships, but each ship belongs to exactly one company, matching
  real-world practice (a vessel has a single commercial operator at a time) and
  `ShipDetail.Owner`/`Manager`, which were already single-valued per ship rather than lists.
  This used to be a many-to-many `ShipCompanyAccess` join table, but that relationship never
  actually occurred in practice, so it was collapsed into a plain `Ships.CompanyId` column.
- All external access is **read-only by design** — there's no `AccessLevel`/permission column
  anywhere in this path.

`AccessScopeResolver.GetAccessibleShipIds(HttpContext)` is the single call site every
REST controller action and MCP tool uses to resolve which ship IDs the current HMAC client may
read — it reads the resolved `AllowedCompanyIds` that the middleware attached to
`HttpContext.Items` (see `HmacAuthContext`), so nothing downstream re-derives auth state.

### Audit logging

Every HMAC request produces **two** rows in `ExternalClientAuditLog`:

1. An **auth-level** row from the middleware itself (`Allowed`/denial reason for the
   authentication decision).
2. A **scope-level** row from `AccessScopeResolver`, recording exactly which companies and
   ship IDs the request was resolved to (e.g. `"Scoped to companies: [1], ships: [1, 2]"`) —
   so the audit trail can prove, after the fact, that a client was restricted to the data it
   should have been restricted to, not just that it was "allowed".

Both rows share the same `ToolName` (the actual MCP tool name for `/mcp` calls, e.g.
`get_logbook_entries`, or `METHOD path` for plain REST calls), so they can be correlated.

### Admin endpoints

Three endpoints are for administrators only, protected by `AdminHmacAuthenticationMiddleware`
on `/api/admin/*` and `/api/audit/external`:

| Endpoint | Purpose |
|---|---|
| `POST /api/admin/clients` | Onboard a new external client for a given company; returns the shared secret **exactly once** |
| `PATCH /api/admin/clients/{clientId}/revoke` | Sets `RevokedAt = UtcNow` for the client; takes effect immediately — its very next request is rejected with 401, since `HmacAuthenticationMiddleware` checks `RevokedAt` on every request |
| `GET /api/audit/external` | Query `ExternalClientAuditLog` (optional filters: `clientId`, `from`, `to`, `allowed`) |

Admin auth uses the **exact same signing contract** external clients use — timestamp window,
`ComputeSignature`, constant-time comparison, replay protection — just verified against a
single well-known admin secret (`Admin:HmacSecret` in `appsettings.json`/
`appsettings.Development.json`) instead of a looked-up per-company `Client`. There's no
`X-Client-Id` for the admin identity, since there's exactly one admin — but there is one extra
required header external clients don't send:

| Header | Value |
|---|---|
| `X-Timestamp` | unix time in whole seconds, same as external clients |
| `X-Nonce` | a client-generated random value (e.g. a GUID), unique per request |
| `X-Signature` | `Base64(HMAC-SHA256(adminSecretBytes, stringToSign))` |

`stringToSign` for the admin identity is (`HmacSignatureService.BuildAdminStringToSign`):

```
{METHOD}\n{PATH}\n{TIMESTAMP}\n{NONCE}\n{BASE64_SHA256_BODY_HASH}
```

External clients don't need a nonce: each has its own unique per-client secret, so two
structurally-identical requests still produce different signatures if the secrets differ.
The admin identity has exactly one shared secret, so without a nonce, two byte-identical
admin requests (same method, path, and body) landing in the same whole-second timestamp would
produce the *exact same signature* and the second would be falsely rejected by the replay
cache as "already used" — a real bug, not just a test artifact, since it would reject a
legitimate second request purely because it happened to look identical to an earlier one.
The nonce fixes this at the root: replay protection for the admin identity is keyed on the
nonce (which the client must make unique per request), not on the signature, so two genuinely
different requests never collide no matter how similar they are otherwise. Reusing a nonce is
still correctly rejected.

This means admin access is no weaker than the external-client model it administers, rather
than a separate, simpler static-bearer-key mechanism.

## Database schema

SQLite, created and seeded automatically on first run from `Data/schema.sql` /
`Data/seed_data.sql` (`DatabaseInitializer.EnsureCreated`).

| Table | Purpose |
|---|---|
| `Companies` | External companies that may be granted read access |
| `Ships` | The fleet — `CompanyId` (NOT NULL, FK to `Companies`) is the owning company; one-to-many, not many-to-many |
| `ShipDetail` | Extended per-ship metadata (IMO number, class, engine, etc.) |
| `ShipLogTable` | The actual logbook entries, one row per log |
| `Clients` | One row per onboarded external client credential (`ClientId` GUID, encrypted secret, primary company, expiry/revocation) |
| `ClientCompanyAccess` | Many-to-many: which companies a given client can read |
| `ExternalClientAuditLog` | Every HMAC request attempt — auth-level and scope-level rows, see above |

`Clients.EncryptedSecret` holds the base64 shared secret encrypted via ASP.NET Core's
`IDataProtector` (reversible, not a one-way hash — HMAC verification needs the raw secret back
to recompute the signature). The same key ring is shared between `ClientOnboardingService`
(encrypts at onboarding) and `HmacAuthenticationMiddleware` (decrypts on every request).

## Running locally

Prerequisites: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
cd mcp-logbook/McpLogbookApi
dotnet run --urls http://localhost:5044
```

Swagger is at `http://localhost:5044/swagger`. The SQLite DB and schema are created
automatically on first run.

### Seeding a demo client

```bash
cd mcp-logbook/McpLogbookApi
dotnet run -- --seed-demo-client              # demo client, scoped to Ocean Star Shipping Co.
dotnet run -- --seed-demo-client-restricted   # second demo client, same company, separate credentials file
```

Both commands scope their client to the same seeded "Ocean Star Shipping Co." — since each
ship now belongs to exactly one company (no more many-to-many `ShipCompanyAccess`), there's no
company left that owns "every ship" for a demo client to see broadly. They're kept as two
separate commands (and credentials files) for anyone who wants two distinct demo client
identities to test with.

Both run in-process (not as a separate call to a running server) so they share the app's exact
`DataProtection` key ring — onboarding from a different process could mint a secret this app's
own middleware then fails to decrypt. Both are idempotent: re-running prints the existing
credentials instead of minting a new client. Credentials (client ID + raw shared secret,
printed **once**) are saved to `Data/demo-client-credentials.json` /
`demo-client-credentials-restricted.json`.

To onboard a client over HTTP instead (e.g. against a real deployment), set `Admin:HmacSecret`
and sign the request per the admin contract above (note the extra `X-Nonce`, and that it's
included in `stringToSign`):

```bash
SECRET_B64="<your Admin:HmacSecret>"
SECRET_HEX=$(echo -n "$SECRET_B64" | base64 -d | xxd -p | tr -d '\n')
BODY='{"clientName":"Acme Corp","primaryCompanyId":1,"expiresAt":null}'
BODY_HASH=$(printf '%s' "$BODY" | openssl dgst -sha256 -binary | base64)
TS=$(date +%s)
NONCE=$(openssl rand -hex 16)
SIGNATURE=$(printf 'POST\n/api/admin/clients\n%s\n%s\n%s' "$TS" "$NONCE" "$BODY_HASH" \
  | openssl dgst -sha256 -mac HMAC -macopt hexkey:"$SECRET_HEX" -binary | base64)

curl -X POST http://localhost:5044/api/admin/clients \
  -H "X-Timestamp: $TS" -H "X-Nonce: $NONCE" -H "X-Signature: $SIGNATURE" \
  -H "Content-Type: application/json" -d "$BODY"
```

### Running the tests

```bash
dotnet test McpLogbookApi.Tests
```

`ExternalClientAuthenticationTests` boots the real app via `WebApplicationFactory`
(`CustomWebApplicationFactory`, a fresh temp-file SQLite DB and a known `Admin:HmacSecret` per
run) and covers: valid-signature company-scoped reads, cross-company isolation,
tampered-signature/stale-timestamp/expired-client/replayed-signature rejection,
admin-HMAC-signature-gated onboarding, revocation, and audit-log access, the two-row
scope-audit behavior described above, and the nonce behavior itself: two structurally-identical
admin requests (same method/path/body, even the same timestamp) both succeed as long as each
has its own nonce, while reusing a nonce is still correctly rejected.

## Demo tooling

- **`HmacTestClient/`** — standalone console app that signs and sends a single HMAC request, as
  a runnable reference implementation of the signing contract for anyone building a client:

  ```bash
  cd HmacTestClient
  dotnet run -- <clientId> <base64Secret> http://localhost:5044 GET /api/mcp/logbooks
  ```

- **`McpHmacProxy/`** — a local stdio↔HTTP proxy. Claude Desktop only speaks MCP over stdio for
  locally-run servers, but this API's `/mcp` endpoint speaks streamable-HTTP and is
  HMAC-protected — this proxy signs every JSON-RPC message Claude Desktop writes to stdin and
  forwards it as a signed HTTP POST, then unwraps the SSE response back to stdout.

  To connect Claude Desktop:
  1. `dotnet run -- --seed-demo-client` (see above) to get a credentials file.
  2. Add an MCP server entry to Claude Desktop's config pointing at `McpHmacProxy`:
     ```json
     {
       "mcpServers": {
         "mcp-logbook": {
           "command": "dotnet",
           "args": ["run", "--project", "<path to McpHmacProxy>", "--",
                     "--credentials", "<path to demo-client-credentials.json>",
                     "--base-url", "http://localhost:5044"]
         }
       }
     }
     ```
  3. Restart Claude Desktop — it should discover `get_logbook_entries`,
     `search_logbook_entries`, and `get_logbook_entry_by_id`.

Both tools share the exact signing logic in `HmacRequestSigner.cs` (linked between the two
projects, not a shared assembly reference — a real external client is a separate codebase
with no access to this repo).

