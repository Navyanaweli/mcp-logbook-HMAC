import { Component, OnInit } from '@angular/core';
import { AuthService } from './services/auth.service';
import { CurrentUser, DemoUser, LogbookEntry, LogbookService } from './services/logbook.service';

// ⚠️ DEMO/LOCAL TESTING ONLY — login here is a mock: picking a seeded
// database user, not a real Microsoft/Entra ID sign-in. See AuthService
// and DemoController for the full explanation.
@Component({
  selector: 'app-root',
  templateUrl: './app.component.html',
  styleUrls: ['./app.component.css']
})
export class AppComponent implements OnInit {
  title = 'NAVTOR Logbook';

  isReady = false;
  isLoggedIn = false;
  loginError: string | null = null;

  demoUsers: DemoUser[] = [];
  selectedEmail = '';

  currentUser: CurrentUser | null = null;

  logbooks: LogbookEntry[] = [];
  loadError: string | null = null;

  searchId = '';
  searchResult: LogbookEntry | null = null;
  searchError: string | null = null;
  searching = false;

  constructor(private auth: AuthService, private logbookService: LogbookService) { }

  async ngOnInit(): Promise<void> {
    try {
      this.demoUsers = await this.logbookService.getUsers().toPromise();
    } catch (err) {
      this.loginError = 'Could not load demo users. Is the API running?';
    }

    this.isLoggedIn = this.auth.isLoggedIn();
    this.isReady = true;

    if (this.isLoggedIn) {
      await this.loadDashboard();
    }
  }

  async login(): Promise<void> {
    this.loginError = null;
    if (!this.selectedEmail) {
      this.loginError = 'Choose a demo user first.';
      return;
    }

    this.auth.login(this.selectedEmail);
    this.isLoggedIn = true;
    await this.loadDashboard();
  }

  logout(): void {
    this.auth.logout();
    this.isLoggedIn = false;
    this.currentUser = null;
    this.logbooks = [];
    this.clearSearch();
  }

  private async loadDashboard(): Promise<void> {
    this.loadError = null;
    const email = this.auth.getEmail();
    if (!email) {
      return;
    }

    try {
      this.currentUser = await this.logbookService.getMe(email).toPromise();

      const response = await this.logbookService.getLogbooks(email).toPromise();
      this.logbooks = response.logbooks;
    } catch (err) {
      if (err && err.status === 404) {
        // No assigned ships — nothing to show, not an error
        this.logbooks = [];
      } else {
        this.loadError = 'Could not load your logbook data.';
      }
    }
  }

  async searchById(): Promise<void> {
    this.searchError = null;
    this.searchResult = null;

    const id = Number(this.searchId);
    if (!this.searchId || Number.isNaN(id)) {
      this.searchError = 'Enter a numeric logbook ID.';
      return;
    }

    const email = this.auth.getEmail();
    if (!email) {
      return;
    }

    this.searching = true;
    try {
      this.searchResult = await this.logbookService.getLogbookById(id, email).toPromise();
    } catch (err) {
      // Deliberately the same message whether the entry doesn't exist or
      // belongs to a ship this demo user isn't assigned to.
      this.searchError = 'Logbook entry not found or you do not have access.';
    } finally {
      this.searching = false;
    }
  }

  // "View" action from the table — the entry is already loaded, no need to re-fetch
  viewEntry(entry: LogbookEntry): void {
    this.searchId = String(entry.shipLogId);
    this.searchResult = entry;
    this.searchError = null;
  }

  clearSearch(): void {
    this.searchId = '';
    this.searchResult = null;
    this.searchError = null;
  }
}
