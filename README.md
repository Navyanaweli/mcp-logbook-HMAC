# MCP Logbook — NAVTOR Ship Logbook System

A demo maritime logbook platform: an ASP.NET Core 8 API (with a real Entra ID OAuth 2.0 path and an MCP tool server) backed by SQLite, plus an Angular dashboard currently wired to a mock demo login.

Ships log operational entries (departures, cargo, weather, bunkering, etc.), and users only ever see the ships they're assigned to — enforced entirely on the backend, the same way regardless of which login path is used.

## What's in here

- **`mcp-logbook/McpLogbookApi/`** — REST API, Entra ID OAuth-protected
- **SQLite** (`logbook.db`) — `Users`, `Ships`, `UserShipRelationship`, `ShipDetail`, `ShipLogTable`
- **MCP tool server** — the same logbook data exposed as callable tools for an AI client, via the official Model Context Protocol C# SDK, behind the same auth as the REST API
- **`logbook-ui/`** — Angular dashboard, see its own [README](logbook-ui/README.md) for frontend detail

## Authentication

Two parallel identity paths exist right now:

- **Real path (Entra ID OAuth 2.0)** — `McpController` and the MCP tools validate Microsoft-issued JWTs and enforce four RBAC roles (`Administrator`, `Superintendent`, `VesselUser`, `ReadOnlyUser`). Fully implemented and tested, but the Angular UI isn't wired to it yet — that needs an Azure Portal app registration.
- **Demo path (mock login)** — `DemoController` has no `[Authorize]`; the UI's login dropdown just picks a seeded user and passes their email as a query param. This is what the current UI uses.

Both paths call the identical `LogbookRepository` methods, so ship-scoped access control is enforced the same way either way — only "who is calling" differs.

## Setup & Run

Prerequisites: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), Node.js + npm (developed against Node 16).

```bash
# Terminal 1 — API
cd mcp-logbook/McpLogbookApi
dotnet run --urls http://localhost:5044

# Terminal 2 — Angular UI
cd logbook-ui
npm install
npx ng serve --port 4200
```

Open `http://localhost:4200`, pick a demo user, and log in. Swagger is at `http://localhost:5044/swagger`.

To exercise the real OAuth path instead (Swagger only, no UI yet), fill in Entra ID values in `mcp-logbook/McpLogbookApi/appsettings.json`:

```json
{
  "AzureAd": {
    "TenantId": "your-entra-tenant-id",
    "ClientId": "your-entra-client-id",
    "Authority": "https://login.microsoftonline.com/your-entra-tenant-id/v2.0"
  }
}
```

then get a token per your app registration's flow and paste it into Swagger's **Authorize** dialog as `Bearer <token>`.

## Testing

```bash
dotnet test McpLogbookApi.Tests
```

17 tests: `AuditServiceTests` (audit logging in isolation) and `AuthorizationTests` (boots the real app via `WebApplicationFactory` with a local test key, covering RBAC per role, ship-scoped isolation, and identical-response behavior for restricted vs. nonexistent records).

## Current status

- ✅ Entra ID OAuth 2.0 + RBAC + audit logging (backend, tested)
- ✅ SQLite-backed, ship-scoped authorization
- ✅ MCP tool server (read-only, same auth as REST)
- ✅ Angular dashboard, on mock demo login
- ⬜ Angular UI not yet wired to real Entra ID/MSAL — needs Azure Portal setup
- ⬜ MCP OAuth protected-resource metadata — not required for the current demo
