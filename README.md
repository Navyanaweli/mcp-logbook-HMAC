# MCP Logbook API — Security PoC

A proof of concept demonstrating secure access to a maritime logbook system using JWT authentication, Role-Based Access Control (RBAC), multi-tenant data isolation, and audit logging — built with ASP.NET Core 8.
![Architecture](architecture.svg)

---

## Project Structure

```
McpLogbook/
└── McpLogbookApi/
    ├── Controllers/
    │   ├── AuthController.cs       # Login + JWT token generation
    │   ├── McpController.cs        # Protected MCP endpoints with RBAC
    │   └── AuditController.cs      # Audit log retrieval
    ├── Models/
    │   └── UserInfo.cs             # User identity model
    ├── Services/
    │   ├── JwtService.cs           # JWT token generation
    │   └── AuditService.cs         # Audit logging
    ├── appsettings.json            # JWT configuration
    └── Program.cs                  # Middleware + DI setup
```

---

## Setup & Run

### Prerequisites
- .NET 8 SDK
- Visual Studio Code or Visual Studio 2022

### Steps

```bash
git clone https://github.com/Navyanaweli/mcp-logbook-poc.git
cd mcp-logbook-poc/McpLogbookApi
dotnet restore
dotnet run
```

Swagger UI will be available at:
```
https://localhost:{port}/swagger
```

---

## Authentication

Send a POST request to `/api/auth/login` with a JSON body:

```json
{
  "username": "alice",
  "role": "Administrator",
  "tenantId": "nordic-shipping"
}
```

You will receive a JWT token in the response. Copy the token and click the **Authorize** button in Swagger to test protected endpoints.

---

## Roles & Permission Matrix

| Endpoint | Administrator | Superintendent | VesselUser | ReadOnlyUser |
|---|---|---|---|---|
| `GET /api/mcp/admin` | ✅ | ❌ | ❌ | ❌ |
| `GET /api/mcp/superintendent` | ✅ | ✅ | ❌ | ❌ |
| `GET /api/mcp/vessel` | ✅ | ✅ | ✅ | ❌ |
| `GET /api/mcp/readonly` | ✅ | ✅ | ✅ | ✅ |
| `GET /api/mcp/logbooks` | ✅ | ✅ | ✅ | ✅ |
| `GET /api/audit/all` | ✅ | ❌ | ❌ | ❌ |
| `GET /api/audit/my-tenant` | ✅ | ✅ | ❌ | ❌ |

---

## Tenant Isolation

Every JWT token contains a `TenantId` claim. The `/api/mcp/logbooks` endpoint filters logbook data strictly by the tenant in the token — users from `nordic-shipping` cannot see data belonging to `pacific-maritime` or `atlantic-fleet`.

---

## Audit Logging

Every request to a protected endpoint is logged with:

| Field | Description |
|---|---|
| User | Username from JWT |
| Role | Role from JWT |
| TenantId | Tenant from JWT |
| Action | Endpoint accessed |
| AuthResult | Authorized / Denied |
| ExecutionStatus | Success / NotFound / Error |
| Timestamp | UTC time of request |

---

## Sample Test Users

| Username | Role | TenantId |
|---|---|---|
| alice | Administrator | nordic-shipping |
| bob | Superintendent | nordic-shipping |
| charlie | VesselUser | pacific-maritime |
| diana | ReadOnlyUser | atlantic-fleet |

Use any of these as the request body for `POST /api/auth/login`.

---

## Tech Stack

- ASP.NET Core 8 Web API
- JWT Bearer Authentication (`Microsoft.AspNetCore.Authentication.JwtBearer`)
- Swagger / Swashbuckle with Bearer token support
- In-memory audit logging (Singleton service)

---

## OAuth 2.0 vs JWT — Design Note

This PoC implements JWT-based authentication directly via the `/api/auth/login` endpoint for simplicity and portability. In a production MCP deployment, this would be replaced by a full OAuth 2.0 / OpenID Connect flow where:

- Tokens are issued by an external authorization server (e.g. Microsoft Entra ID, Auth0)
- The MCP server acts as a **resource server** — it only validates tokens, never issues them
- Clients authenticate via authorization code flow or client credentials flow
- The `/api/auth/login` endpoint is removed entirely

The JWT validation logic in `Program.cs` and `JwtService.cs` is already structured to support this — swapping the token issuer from the local service to Entra ID requires only updating `appsettings.json`:

```json
"Jwt": {
  "Issuer": "https://login.microsoftonline.com/{tenant-id}/v2.0",
  "Audience": "api://{client-id}"
}
```

---

## Microsoft Entra ID Integration

To connect this PoC to Microsoft Entra ID:

1. Register an app in [Azure Portal](https://portal.azure.com) → Azure Active Directory → App Registrations
2. Set the Audience to your app's `client-id`
3. Update `appsettings.json` with the Entra issuer URL above
4. Remove `AuthController.cs` — tokens are now issued by Entra
5. Users authenticate via `https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token`