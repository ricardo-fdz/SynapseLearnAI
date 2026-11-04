import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import type { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';
import type {
  AppliedMemoryChangeSummary,
  ContextLoadProfile,
  Message,
  PagedResult,
  SendMessageResponse,
} from '../models';

export interface MessageRequest {
  sessionId: number;
  role: Message['role'];
  content: string;
}

export type ChatStreamEvent =
  | { type: 'user_message'; data: Message }
  | {
      type: 'tool_call_started';
      data: { iteration: number; toolName: string; key?: string | null; operation?: string | null };
    }
  | {
      type: 'tool_call_completed';
      data: {
        iteration: number;
        toolName: string;
        key?: string | null;
        operation?: string | null;
        success?: boolean | null;
      };
    }
  | { type: 'token_delta'; data: { delta: string } }
  | { type: 'turn_completed'; data: SendMessageResponse }
  | {
      type: 'error';
      data: {
        statusCode: number;
        message: string;
        userMessage?: Message | null;
        appliedMemoryChanges?: AppliedMemoryChangeSummary[] | null;
      };
    };

export function parseSseFrame(rawFrame: string): ChatStreamEvent | null {
  const lines = rawFrame.split('\n');
  let eventType = 'message';
  const dataLines: string[] = [];

  for (const line of lines) {
    if (line.startsWith('event:')) {
      eventType = line.slice('event:'.length).trim();
    } else if (line.startsWith('data:')) {
      dataLines.push(line.slice('data:'.length).trim());
    }
  }

  if (dataLines.length === 0) {
    return null;
  }

  try {
    const data = JSON.parse(dataLines.join('\n'));
    return { type: eventType, data } as ChatStreamEvent;
  } catch {
    return null;
  }
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

  sendMessage(
    sessionId: number,
    content: string | null,
    profile: ContextLoadProfile,
    retryUserMessageId?: number | null,
  ): Observable<SendMessageResponse> {
    return this.http.post<SendMessageResponse>(`${this.sessionsUrl}/${sessionId}/messages`, {
      content,
      profile,
      ...(retryUserMessageId ? { retryUserMessageId } : {}),
    });
  }

  async streamMessage(
    sessionId: number,
    content: string | null,
    profile: ContextLoadProfile,
    onEvent: (event: ChatStreamEvent) => void,
    signal?: AbortSignal,
    retryUserMessageId?: number | null,
  ): Promise<void> {
    const response = await fetch(`${this.sessionsUrl}/${sessionId}/messages/stream`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        Accept: 'text/event-stream',
      },
      body: JSON.stringify({
        content,
        profile,
        ...(retryUserMessageId ? { retryUserMessageId } : {}),
      }),
      signal,
    });

    if (!response.ok || !response.body) {
      let detail = `HTTP ${response.status}`;
      try {
        const errJson = (await response.json()) as { detail?: string; message?: string };
        detail = errJson.detail || errJson.message || detail;
      } catch {
        // Ignore non-JSON body
      }
      onEvent({
        type: 'error',
        data: { statusCode: response.status || 502, message: detail },
      });
      return;
    }

    const reader = response.body.getReader();
    const decoder = new TextDecoder('utf-8');
    let buffer = '';

    while (true) {
      const { value, done } = await reader.read();
      if (done) {
        buffer += decoder.decode();
        break;
      }

      buffer += decoder.decode(value, { stream: true });
      buffer = buffer.replace(/\r\n/g, '\n');

      let boundary = buffer.indexOf('\n\n');
      while (boundary >= 0) {
        const rawFrame = buffer.slice(0, boundary).trim();
        buffer = buffer.slice(boundary + 2);
        if (rawFrame) {
          const parsed = parseSseFrame(rawFrame);
          if (parsed) {
            onEvent(parsed);
          }
        }
        boundary = buffer.indexOf('\n\n');
      }
    }

    const remaining = buffer.trim();
    if (remaining) {
      const parsed = parseSseFrame(remaining);
      if (parsed) {
        onEvent(parsed);
      }
    }
  }
}
