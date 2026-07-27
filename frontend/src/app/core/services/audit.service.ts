import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import type { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import type { MemoryChange, PagedResult } from '../models';

@Injectable({ providedIn: 'root' })
export class AuditService {
  private readonly http = inject(HttpClient);
  private readonly apiUrl = environment.apiUrl;

  getTutorMemoryChanges(
    tutorId: number,
    page = 1,
    pageSize = 20,
  ): Observable<PagedResult<MemoryChange>> {
    return this.http.get<PagedResult<MemoryChange>>(
      `${this.apiUrl}/api/tutors/${tutorId}/memory-changes`,
      { params: { page, pageSize } },
    );
  }

  getMemoryEntryChanges(
    memoryEntryId: number,
    page = 1,
    pageSize = 20,
  ): Observable<PagedResult<MemoryChange>> {
    return this.http.get<PagedResult<MemoryChange>>(
      `${this.apiUrl}/api/memory-entries/${memoryEntryId}/memory-changes`,
      { params: { page, pageSize } },
    );
  }

  getMemoryChanges(): Observable<MemoryChange[]> {
    return this.http.get<MemoryChange[]>(`${this.apiUrl}/api/memory-changes`);
  }

  getMemoryChange(id: number): Observable<MemoryChange> {
    return this.http.get<MemoryChange>(`${this.apiUrl}/api/memory-changes/${id}`);
  }
}
