# MCP Logbook — NAVTOR Ship Logbook System

A demo maritime logbook system: an ASP.NET Core 8 API (with a real Entra ID OAuth 2.0 path and an MCP tool server) backed by SQLite, plus an Angular dashboard currently wired to a mock demo login. Each part below is self-contained — read top to bottom for the full picture, or jump to the part you need.

![Architecture](architecture.svg)

---

## Table of Contents

- [Part 1 — What This Project Is](#part-1--what-this-project-is)
- [Part 2 — Architecture at a Glance](#part-2--architecture-at-a-glance)
- [Part 3 — Backend: ASP.NET Core API](#part-3--backend-aspnet-core-api)
- [Part 4 — Database: SQLite Schema](#part-4--database-sqlite-schema)
- [Part 5 — MCP Server & Tools](#part-5--mcp-server--tools)
- [Part 6 — Authentication & Authorization](#part-6--authentication--authorization)
- [Part 7 — Frontend: Angular UI](#part-7--frontend-angular-ui)
- [Part 8 — Setup & Run](#part-8--setup--run)
- [Part 9 — Testing](#part-9--testing)
- [Part 10 — Configuration Reference](#part-10--configuration-reference)
- [Part 11 — Current Status & What's Not Done Yet](#part-11--current-status--whats-not-done-yet)

---

## Part 1 — What This Project Is

This simulates a maritime logbook platform where ships log operational entries (departures, cargo, weather, bunkering, etc.), and users only see the ships they're assigned to. It has four moving pieces:

1. **A REST API** (`mcp-logbook/McpLogbookApi`) — real, Entra ID OAuth-protected endpoints
2. **A SQLite database** — `Users`, `Ships`, `UserShipRelationship`, `ShipDetail`, `ShipLogTable`
3. **An MCP tool server** — the same logbook data exposed as callable tools for an AI client, via the official Model Context Protocol C# SDK
4. **An Angular dashboard** (`logbook-ui`) — currently uses a **mock demo login** (pick a seeded user from a dropdown), not real Microsoft sign-in yet — see [Part 6](#part-6--authentication--authorization)

All four share one rule: a user only ever sees logbook entries for ships they're assigned to, enforced entirely on the backend.

---

## Part 2 — Architecture at a Glance

```
Angular UI (logbook-ui)
   │
   │  demo login (mock) ──────────┐
   │                              │
   ▼                              ▼
DemoController              McpController + LogbookTools (MCP)
(unauthenticated,            (Entra ID OAuth-protected)
 demo/testing only)                 │
   │                                │
   └──────────────┬─────────────────┘
                   ▼
           LogbookRepository
                   │
                   ▼
         SQLite (logbook.db)
   Users · Ships · UserShipRelationship
        · ShipDetail · ShipLogTable
```

Both the demo path and the real OAuth path converge on the same `LogbookRepository` — ship-scoped access control is identical either way; only the "who is calling" step differs. See `uml-diagram.puml` for a full class/schema diagram of the backend.

---

## Part 3 — Backend: ASP.NET Core API

**Path:** `mcp-logbook/McpLogbookApi/`

```
McpLogbookApi/
├── Controllers/
│   ├── McpController.cs      # Real, OAuth-protected endpoints
│   ├── AuditController.cs    # Audit log retrieval
│   └── DemoController.cs     # ⚠️ Unauthenticated demo-login endpoints
├── Services/
│   ├── LogbookRepository.cs  # All ship-scoped SQLite queries
│   └── AuditService.cs       # Records/retrieves audit log entries
├── Tools/
│   └── LogbookTools.cs       # MCP tools (see Part 5)
├── Data/
│   ├── schema.sql / seed_data.sql
│   └── DatabaseInitializer.cs
├── Models/                   # ShipLogEntry, AssignedShip, DemoUser, AuditEntry records
├── Middleware/
│   └── ObservabilityMiddleware.cs   # Logs 401/403 before they reach a controller
├── appsettings.json
└── Program.cs                 # DI, auth, CORS, MCP registration, middleware pipeline
```

**Tech stack:** ASP.NET Core 8, `Microsoft.Data.Sqlite` (raw ADO.NET, no ORM), `ModelContextProtocol`/`ModelContextProtocol.AspNetCore`, JWT Bearer + Entra ID, Swagger/Swashbuckle, xUnit.

### McpController — real endpoints (`/api/mcp`, policy `ReadOnlyUp` unless noted)

| Method | Route | Description |
|---|---|---|
| GET | `/api/mcp/me` | Current user's identity, role, and assigned ships |
| GET | `/api/mcp/readonly` | Demo/role-check endpoint, no real data |
| GET | `/api/mcp/logbooks` | Logbook entries for the caller's assigned ships |
| GET | `/api/mcp/logbooks/{id}` | Single entry — identical 404 whether it doesn't exist or isn't yours |

### AuditController (`/api/audit`)

| Method | Route | Policy | Description |
|---|---|---|---|
| GET | `/api/audit/all` | `AdminOnly` | Full audit log, all users |
| GET | `/api/audit/my-tenant` | `SuperintendentUp` | Audit log scoped to caller's tenant claim |

### DemoController — ⚠️ demo/testing only (`/api/demo`, no auth)

See [Part 6](#part-6--authentication--authorization) for why this exists. Mirrors `McpController`'s shape exactly, but takes `email` as an explicit query parameter instead of reading it from a validated token:

| Method | Route | Description |
|---|---|---|
| GET | `/api/demo/users` | Lists all seeded demo users (for the UI's login dropdown) |
| GET | `/api/demo/me?email=` | Identity/role/ships for the given demo user |
| GET | `/api/demo/logbooks?email=` | Ship-scoped logbook list for the given demo user |
| GET | `/api/demo/logbooks/{id}?email=` | Single entry, same identical-404 behavior as the real endpoint |

---

## Part 4 — Database: SQLite Schema

**Path:** `mcp-logbook/McpLogbookApi/Data/schema.sql` (+ `seed_data.sql`)

| Table | Purpose |
|---|---|
| `Users` | `UserId`, `UserName`, `Email` (unique) |
| `Ships` | `ShipId`, `ShipName` |
| `UserShipRelationship` | Many-to-many join: which users can access which ships |
| `ShipDetail` | Extended ship metadata (IMO number, flag state, tonnage, etc.) — one row per ship |
| `ShipLogTable` | The actual logbook entries: `ShipLogId`, `ShipId`, `LogText`, `LogDate` |

`DatabaseInitializer.EnsureCreated()` runs both `.sql` files once, automatically, the first time the app starts and `logbook.db` doesn't exist yet — no manual migration step. Seeded demo users: **Alice Mercer** (2 ships), **Rahul Verma** (1 ship), **Sofia Nunez** (1 ship).

Every read — REST, MCP tool, or demo — ultimately runs one of four `LogbookRepository` queries: assigned ship IDs, assigned ships (with names), logs for a set of ships, or a single log by ID. None of them ever return data for a ship the caller isn't assigned to.

---

## Part 5 — MCP Server & Tools

**Path:** `mcp-logbook/McpLogbookApi/Tools/LogbookTools.cs`

Beyond the REST API, the same logbook data is exposed via the [Model Context Protocol](https://modelcontextprotocol.io) so an AI client (not just a browser) can call it directly as tools:

```csharp
builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.Stateless = true)
    .WithToolsFromAssembly();
...
app.MapMcp("/mcp").RequireAuthorization("ReadOnlyUp");
```

| Tool (registered name) | What it does |
|---|---|
| `get_logbook_entries` | Lists entries for the caller's assigned ships |
| `search_logbook_entries` | Same, filtered by a text query against the log entry |
| `get_logbook_entry_by_id` | Single entry — returns nothing if it doesn't exist or isn't the caller's ship |

**Read-only by design** — there is no create/update/delete tool anywhere in the project. `/mcp` sits behind the exact same Entra ID JWT Bearer auth and `ReadOnlyUp` policy as the REST endpoints; it's a second protocol on top of the same authorization, not a separate security model.

---

## Part 6 — Authentication & Authorization

There are **two parallel identity paths** right now, and they must not be confused:

### The real path — Entra ID OAuth 2.0 (McpController, LogbookTools)

- `Program.cs` configures `AddAuthentication().AddJwtBearer(...)` with `Authority`/`Audience` pointed at your Entra ID tenant. The API never issues tokens — it only validates ones Microsoft already signed.
- Four RBAC policies, checked via the token's `roles` claim: `AdminOnly`, `SuperintendentUp`, `ReadOnlyUp` (a fourth-tier `VesselUser` role also exists but no longer has its own dedicated endpoint).
- This is fully implemented and tested, but **the Angular UI is not wired to it yet** — that requires Azure Portal setup (App Registration redirect URI, exposed API scope, permissions/consent) that hasn't been done in this environment.

### The demo path — mock login (DemoController, current Angular UI)

- ⚠️ **Not real authentication.** The UI shows a dropdown of seeded database users; picking one just stores their email client-side. No password, no token, no Microsoft account.
- `DemoController` deliberately has no `[Authorize]` — it trusts the `email` query parameter directly instead of a validated claim.
- **What is *not* mocked:** ship-level access control. Both paths call the identical `LogbookRepository` methods, so "can this user see this ship's logs" is enforced by the same real logic either way — only the "who is this user" step is faked.
- This exists purely so the UI can be developed/tested without Azure setup. It should never be exposed outside local development.

### Shared security property

Both `GET /api/mcp/logbooks/{id}` and `GET /api/demo/logbooks/{id}` return the **exact same response** whether a logbook entry doesn't exist at all or exists but belongs to a ship the caller isn't assigned to (`"Logbook entry not found."` / `"Logbook entry not found or you do not have access."`). This is intentional — it prevents a caller from ever confirming that a restricted record exists.

---

## Part 7 — Frontend: Angular UI

**Path:** `logbook-ui/` — see its own [README](logbook-ui/README.md) for full detail; summary here:

- Angular 9, plain `HttpClient`, dark NAVTOR-themed dashboard
- Login screen: dropdown of demo users → dashboard showing username, role, assigned ships, entry count, a searchable/browsable logbook table
- Calls **only** `/api/demo/*` today (see Part 6)
- Built to make real Entra ID/MSAL login easy to add later as a **second** login option alongside the demo dropdown, not a replacement — see the extension-point comments in `logbook-ui/src/app/services/auth.service.ts` and `app.component.html`

---

## Part 8 — Setup & Run

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Node.js + npm (Angular 9 tooling; developed against Node 16)

### Run both together

```bash
# Terminal 1 — API
cd mcp-logbook/McpLogbookApi
dotnet run --urls http://localhost:5044

# Terminal 2 — Angular UI
cd logbook-ui
npm install
npx ng serve --port 4200
```

Open `http://localhost:4200`, pick a demo user, and log in. Swagger for the API is at `http://localhost:5044/swagger`. CORS on the backend is scoped specifically to `http://localhost:4200`.

### Running the real OAuth path instead (Swagger only, no UI yet)

Follow [Part 10](#part-10--configuration-reference) to fill in real Entra ID values, then obtain a token per your app registration's flow and paste it into Swagger's **Authorize** dialog as `Bearer <token>`.

---

## Part 9 — Testing

**Path:** `McpLogbookApi.Tests/`

```bash
dotnet test McpLogbookApi.Tests
```

17 tests, all passing:
- **`AuditServiceTests.cs`** — `AuditService` in isolation (log storage, tenant filtering)
- **`AuthorizationTests.cs`** — boots the real app via `WebApplicationFactory`, with Entra ID validation swapped for a local test key so it runs fully offline. Covers: unauthenticated requests, RBAC per role, ship-scoped isolation (including a multi-ship user), identical-response behavior for restricted vs. nonexistent logbook IDs, and audit endpoint access control.

---

## Part 10 — Configuration Reference

### Backend — `mcp-logbook/McpLogbookApi/appsettings.json`

```json
{
  "AzureAd": {
    "TenantId": "your-entra-tenant-id",
    "ClientId": "your-entra-client-id",
    "Authority": "https://login.microsoftonline.com/your-entra-tenant-id/v2.0"
  },
  "ConnectionStrings": {
    "LogbookDb": "Data Source=Data/logbook.db"
  },
  "AuditLog": {
    "FilePath": "audit-log.jsonl"
  }
}
```

To connect the real OAuth path to an actual Entra ID tenant: register an app in [Azure Portal](https://portal.azure.com), add the four App Roles (`Administrator`, `Superintendent`, `VesselUser`, `ReadOnlyUser`), assign them to test users, then fill in the values above.

### Frontend — `logbook-ui/src/environments/environment.ts`

```ts
export const environment = {
  production: false,
  apiBaseUrl: 'http://localhost:5044'
};
```

No Azure values needed today — the demo login doesn't require any. This is where MSAL config would be reintroduced later.

---

## Part 11 — Current Status & What's Not Done Yet

- ✅ Real Entra ID OAuth 2.0 + RBAC + audit logging (backend, fully implemented and tested)
- ✅ SQLite-backed, ship-scoped authorization (`UserShipRelationship`)
- ✅ MCP tool server (read-only, same auth as REST)
- ✅ Angular dashboard, currently on mock demo login
- ⬜ Angular UI not yet wired to real Entra ID/MSAL — needs Azure Portal setup (SPA redirect URI, exposed API scope, permissions/consent) and re-adding `@azure/msal-browser`
- ⬜ MCP OAuth protected-resource metadata (`.well-known/oauth-protected-resource`) — not required for the current demo, would improve auto-discovery for third-party MCP clients later
