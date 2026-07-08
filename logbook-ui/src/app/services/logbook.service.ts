import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';

export interface DemoUser {
  email: string;
  userName: string;
}

export interface AssignedShip {
  shipId: number;
  shipName: string;
}

export interface CurrentUser {
  username: string;
  role: string;
  ships: AssignedShip[];
}

export interface LogbookEntry {
  shipLogId: number;
  shipId: number;
  shipName: string;
  logText: string;
  logDate: string;
}

export interface LogbooksResponse {
  accessedBy: string;
  shipIds: number[];
  count: number;
  logbooks: LogbookEntry[];
}

// Thin HTTP wrapper — no ship/access logic here. The backend's DemoController
// (demo-only) + LogbookRepository own identity lookup and ship-access checks;
// Angular only displays whatever it returns.
//
// ⚠️ Calls /api/demo/*, the unauthenticated demo surface — see DemoController.cs.
@Injectable({
  providedIn: 'root'
})
export class LogbookService {
  private readonly base = `${environment.apiBaseUrl}/api/demo`;

  constructor(private http: HttpClient) { }

  getUsers(): Observable<DemoUser[]> {
    return this.http.get<DemoUser[]>(`${this.base}/users`);
  }

  getMe(email: string): Observable<CurrentUser> {
    return this.http.get<CurrentUser>(`${this.base}/me`, { params: new HttpParams().set('email', email) });
  }

  getLogbooks(email: string): Observable<LogbooksResponse> {
    return this.http.get<LogbooksResponse>(`${this.base}/logbooks`, { params: new HttpParams().set('email', email) });
  }

  getLogbookById(id: number, email: string): Observable<LogbookEntry> {
    return this.http.get<LogbookEntry>(`${this.base}/logbooks/${id}`, { params: new HttpParams().set('email', email) });
  }
}
