import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';

import { MessageService, parseSseFrame, type ChatStreamEvent } from './message.service';

describe('MessageService SSE streaming', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient()],
    });
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('parses individual SSE frames cleanly', () => {
    const parsed = parseSseFrame(
      'event: tool_call_started\ndata: {"iteration":1,"toolName":"leer_memoria","key":"mapa_dominio"}',
    );
    expect(parsed).toEqual({
      type: 'tool_call_started',
      data: { iteration: 1, toolName: 'leer_memoria', key: 'mapa_dominio' },
    });
  });

  it('reassembles multi-chunk SSE streams across arbitrary byte boundaries', async () => {
    const service = TestBed.inject(MessageService);
    const encoder = new TextEncoder();

    const chunks = [
      'event: user_message\ndata: {"id":10,"sessionId":1,"role":"user","content":"Hola","cr',
      'eatedAtUtc":"2026-07-01T00:00:00Z"}\n\nevent: token_delta\ndata: {"delta":"Hola "}\n\n',
      'event: token_delta\ndata: {"delta":"mundo"}\n\n',
    ];

    const stream = new ReadableStream<Uint8Array>({
      start(controller) {
        for (const chunk of chunks) {
          controller.enqueue(encoder.encode(chunk));
        }
        controller.close();
      },
    });

    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(stream, {
        status: 200,
        headers: { 'Content-Type': 'text/event-stream' },
      }),
    );

    const received: ChatStreamEvent[] = [];
    await service.streamMessage(1, 'Hola', 'Standard', (evt) => received.push(evt));

    expect(received.map((e) => e.type)).toEqual(['user_message', 'token_delta', 'token_delta']);
    expect(received[1]).toEqual({ type: 'token_delta', data: { delta: 'Hola ' } });
    expect(received[2]).toEqual({ type: 'token_delta', data: { delta: 'mundo' } });
  });
});
