# NAVTOR Logbook UI

Angular dashboard for the [MCP Logbook API](../mcp-logbook/McpLogbookApi) — sign in, see which ships you're authorized for, and browse/search their logbook entries. The backend enforces every access rule; this app only displays what the backend returns.

## Current auth mode: demo login (not real OAuth)

⚠️ **Login right now is a mock**, built to work without any Azure/Entra ID setup:

- The login screen shows a dropdown of the seeded demo users (`Data/seed_data.sql` in the API project — Alice Mercer, Rahul Verma, Sofia Nunez).
- Picking a user just stores their email in `sessionStorage` — there's no password, no token, no Microsoft account involved.
- The backend endpoint behind this (`GET /api/demo/*` in `DemoController.cs`) is **deliberately unauthenticated** to match. It reuses the exact same ship-scoped authorization logic (`LogbookRepository`) as the real API, so access control itself isn't mocked — only the "who is signed in" step is.

The **real**, Entra ID OAuth-protected API (`McpController`, `LogbookTools`, MCP server) already exists in the backend, untouched, behind proper JWT Bearer validation. Adding real Microsoft sign-in back to this UI is meant to be a second path alongside the demo login, not a replacement of it — see the extension-point comments in `src/app/services/auth.service.ts` and `src/app/app.component.html` for where that slots in.

## Tech stack

- Angular 9 (NgModule-based, not standalone components)
- Plain `HttpClient` — no state management library
- Dark, NAVTOR-themed UI (`app.component.css`)

## Project layout

```
src/app/
├── services/
│   ├── auth.service.ts       # Demo login state (sessionStorage) — see note above
│   └── logbook.service.ts    # Calls the API's /api/demo/* endpoints
├── app.component.ts          # Login + dashboard state, search-by-ID logic
├── app.component.html        # Login screen / dashboard views
└── app.module.ts
```

## Running it

This UI needs the API running alongside it.

```bash
# Terminal 1 — API (from repo root)
cd ../mcp-logbook/McpLogbookApi
dotnet run --urls http://localhost:5044

# Terminal 2 — this app
cd logbook-ui
npm install
npx ng serve --port 4200
```

Open `http://localhost:4200`. The API must run on `http://localhost:5044` (or update `src/environments/environment.ts`'s `apiBaseUrl`) — CORS on the backend is scoped specifically to `http://localhost:4200`.

## Development

Run `ng serve` for a dev server; the app reloads automatically on source changes.

Run `ng build` to build for production; output goes to `dist/logbook-ui`.

Run `ng test` to run unit tests via Karma (requires Chrome).
