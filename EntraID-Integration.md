# Microsoft Entra ID Integration Guide

This document explains how to connect the MCP Logbook API to Microsoft Entra ID (formerly Azure Active Directory) as a production OAuth 2.0 provider.

---

## How it works

In this PoC, the `/api/auth/login` endpoint acts as a local token issuer for testing. In production:

- Microsoft Entra ID issues JWT tokens
- The MCP Logbook API acts as a **resource server** — it only validates tokens
- The `/api/auth/login` endpoint is removed entirely
- Clients authenticate directly against Entra ID

---

## Step 1 — Register the app in Azure Portal

1. Go to [portal.azure.com](https://portal.azure.com)
2. Navigate to **Azure Active Directory → App Registrations → New Registration**
3. Name it `MCP Logbook API`
4. Set Supported account types to **Single tenant**
5. Click **Register**
6. Copy the **Application (client) ID** and **Directory (tenant) ID**

---

## Step 2 — Expose an API

1. In your app registration go to **Expose an API**
2. Set the Application ID URI to `api://{client-id}`
3. Add a scope called `Logbook.Access`

---

## Step 3 — Add app roles

In your app registration go to **App Roles → Create app role** and add:

| Display name | Value | Description |
|---|---|---|
| Administrator | Administrator | Full system access |
| Superintendent | Superintendent | Inspect and approve logbooks |
| Vessel User | VesselUser | Submit and edit logbook entries |
| Read-Only User | ReadOnlyUser | View logbook records |

---

## Step 4 — Update `appsettings.json`

Replace the `Jwt` section with:

```json
"Jwt": {
  "Issuer": "https://login.microsoftonline.com/{your-tenant-id}/v2.0",
  "Audience": "api://{your-client-id}",
  "ExpiryHours": "2"
}
```

Remove the `SecretKey` — Entra ID uses asymmetric signing keys fetched automatically.

---

## Step 5 — Update `Program.cs`

Replace the JWT validation block with:

```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
.AddJwtBearer(options =>
{
    options.Authority = builder.Configuration["Jwt:Issuer"];
    options.Audience  = builder.Configuration["Jwt:Audience"];
});
```

Entra ID publishes its public signing keys at the `Authority` URL — ASP.NET Core fetches and rotates them automatically. No `SecretKey` needed.

---

## Step 6 — Remove `AuthController.cs`

The local login endpoint is no longer needed. Clients authenticate via:

```
POST https://login.microsoftonline.com/{tenant-id}/oauth2/v2.0/token
```

With body:
```
client_id={client-id}
client_secret={client-secret}
scope=api://{client-id}/Logbook.Access
grant_type=client_credentials
```

---

## Summary

| Component | PoC (current) | Production (Entra ID) |
|---|---|---|
| Token issuer | `/api/auth/login` | Microsoft Entra ID |
| Token validation | Local secret key | Entra public keys (auto-fetched) |
| User roles | JWT claim from login body | Entra app roles assigned to users |
| Tenant isolation | `TenantId` claim from login body | `tid` claim from Entra token |