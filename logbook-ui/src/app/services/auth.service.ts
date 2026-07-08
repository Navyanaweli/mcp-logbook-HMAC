import { Injectable } from '@angular/core';
import { BehaviorSubject } from 'rxjs';

const STORAGE_KEY = 'demoUserEmail';

// ⚠️ DEMO/LOCAL TESTING ONLY — this is a mock login, not real authentication.
// There is no Microsoft/Entra ID sign-in and no access token involved. The
// user just picks one of the seeded database users, and the backend's
// DemoController (also unauthenticated, by design) uses that email to look
// up ship access via the real UserShipRelationship-backed authorization
// logic — only the "who is calling" step is mocked, not the access control.
//
// Extension point for later: when real Entra ID login is added back, it
// belongs here as a second, parallel path (e.g. loginWithMicrosoft() using
// @azure/msal-browser) alongside login(email), not a replacement of it —
// this class can then expose both, with the caller (app.component) offering
// the user a choice. The real McpController + LogbookTools MCP flow already
// exists, untouched, behind Entra ID JWT Bearer auth — this service is the
// only thing that needs a second identity path bolted on.
@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private emailSubject = new BehaviorSubject<string | null>(sessionStorage.getItem(STORAGE_KEY));
  email$ = this.emailSubject.asObservable();

  isLoggedIn(): boolean {
    return this.emailSubject.value !== null;
  }

  getEmail(): string | null {
    return this.emailSubject.value;
  }

  login(email: string): void {
    sessionStorage.setItem(STORAGE_KEY, email);
    this.emailSubject.next(email);
  }

  logout(): void {
    sessionStorage.removeItem(STORAGE_KEY);
    this.emailSubject.next(null);
  }
}
