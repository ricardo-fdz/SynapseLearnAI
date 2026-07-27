import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, type Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import type { MemoryEntry } from '../models';

export interface MemoryEntryRequest {
  tutorId: number;
  key: string;
  valueJson: string;
  schemaVersion: number;
}

@Injectable({ providedIn: 'root' })
export class MemoryService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/api/memory-entries`;

  getMemoryEntries(): Observable<MemoryEntry[]> {
    return this.http.get<MemoryEntry[]>(this.baseUrl);
  }

  getMemoryEntriesByTutor(tutorId: number): Observable<MemoryEntry[]> {
    return this.getMemoryEntries().pipe(
      map((entries) =>
        entries
          .filter((entry) => entry.tutorId === tutorId)
          .sort((first, second) => first.key.localeCompare(second.key)),
      ),
    );
  }

  getMemoryEntry(id: number): Observable<MemoryEntry> {
    return this.http.get<MemoryEntry>(`${this.baseUrl}/${id}`);
  }

  createMemoryEntry(request: MemoryEntryRequest): Observable<MemoryEntry> {
    return this.http.post<MemoryEntry>(this.baseUrl, request);
  }

  updateMemoryEntry(id: number, request: MemoryEntryRequest): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/${id}`, request);
  }

  deleteMemoryEntry(id: number): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${id}`);
  }
}
