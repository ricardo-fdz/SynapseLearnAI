import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import type { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import type { StudySession } from '../models';

export interface StudySessionRequest {
  tutorId: number;
  name: string;
  goal: string;
}

@Injectable({ providedIn: 'root' })
export class SessionService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/api/study-sessions`;

  getSessions(): Observable<StudySession[]> {
    return this.http.get<StudySession[]>(this.baseUrl);
  }

  getSession(id: number): Observable<StudySession> {
    return this.http.get<StudySession>(`${this.baseUrl}/${id}`);
  }

  createSession(request: StudySessionRequest): Observable<StudySession> {
    return this.http.post<StudySession>(this.baseUrl, request);
  }

  updateSession(id: number, request: StudySessionRequest): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/${id}`, request);
  }

  deleteSession(id: number): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${id}`);
  }
}
