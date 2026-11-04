import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import type { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import type { ContextLoadProfile, LlmProfilesCatalog, Tutor } from '../models';

export interface InitialStudentProfile {
  alias?: string | null;
  lenguaje_principal?: string | null;
  objetivo_declarado?: string | null;
  estilo_aprendizaje?: {
    prefiere?: string | null;
    ritmo_sesion?: string | null;
    reaccion_ante_errores?: string | null;
    nivel_autonomia?: string | null;
  } | null;
  preferencias_comunicacion?: {
    idioma?: string | null;
    tono_tutor?: string | null;
  } | null;
}

export interface CreateTutorRequest {
  name: string;
  description: string;
  systemPromptContent: string;
  geminiModel?: string | null;
  llmProfile?: string | null;
  initialStudentProfile?: InitialStudentProfile | null;
}

export interface TutorRequest {
  name: string;
  description: string;
  systemPromptContent: string;
  geminiModel?: string | null;
  llmProfile?: string | null;
}

export interface TutorMemoryPatchRequest {
  key: 'perfil_estudiante';
  operation: 'Set';
  path: string;
  value: string;
  reason: string;
}

@Injectable({ providedIn: 'root' })
export class TutorService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/api/tutors`;
  private readonly profilesUrl = `${environment.apiUrl}/api/llm-profiles`;

  getTutors(): Observable<Tutor[]> {
    return this.http.get<Tutor[]>(this.baseUrl);
  }

  getTutor(id: number): Observable<Tutor> {
    return this.http.get<Tutor>(`${this.baseUrl}/${id}`);
  }

  getLlmProfiles(): Observable<LlmProfilesCatalog> {
    return this.http.get<LlmProfilesCatalog>(this.profilesUrl);
  }

  createTutor(request: CreateTutorRequest): Observable<Tutor> {
    return this.http.post<Tutor>(this.baseUrl, request);
  }

  updateTutor(id: number, request: TutorRequest): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/${id}`, request);
  }

  deleteTutor(id: number): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${id}`);
  }

  patchTutorMemory(id: number, request: TutorMemoryPatchRequest): Observable<unknown> {
    return this.http.post<unknown>(`${this.baseUrl}/${id}/memory-patch`, request);
  }

  getPromptPreview(
    id: number,
    profile: ContextLoadProfile,
    sessionId?: number | null,
  ): Observable<string> {
    const params: Record<string, string | number> = { profile };
    if (sessionId) {
      params['sessionId'] = sessionId;
    }

    return this.http.get(`${this.baseUrl}/${id}/prompt-preview`, {
      params,
      responseType: 'text',
    });
  }
}
