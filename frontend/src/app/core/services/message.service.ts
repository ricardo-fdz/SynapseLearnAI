import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import type { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import type { ContextLoadProfile, Message, PagedResult, SendMessageResponse } from '../models';

export interface MessageRequest {
  sessionId: number;
  role: Message['role'];
  content: string;
}

@Injectable({ providedIn: 'root' })
export class MessageService {
  private readonly http = inject(HttpClient);
  private readonly messagesUrl = `${environment.apiUrl}/api/messages`;
  private readonly sessionsUrl = `${environment.apiUrl}/api/sessions`;

  getMessages(): Observable<Message[]> {
    return this.http.get<Message[]>(this.messagesUrl);
  }

  getSessionMessages(
    sessionId: number,
    page = 1,
    pageSize = 50,
  ): Observable<PagedResult<Message>> {
    return this.http.get<PagedResult<Message>>(`${this.sessionsUrl}/${sessionId}/messages`, {
      params: { page, pageSize },
    });
  }

  getMessage(id: number): Observable<Message> {
    return this.http.get<Message>(`${this.messagesUrl}/${id}`);
  }

  createMessage(request: MessageRequest): Observable<Message> {
    return this.http.post<Message>(this.messagesUrl, request);
  }

  sendMessage(sessionId: number, content: string, profile: ContextLoadProfile): Observable<SendMessageResponse> {
    return this.http.post<SendMessageResponse>(`${this.sessionsUrl}/${sessionId}/messages`, { content, profile });
  }
}
