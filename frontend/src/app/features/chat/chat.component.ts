import { HttpErrorResponse } from '@angular/common/http';
import {
  AfterViewInit,
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  OnDestroy,
  ViewChild,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { finalize } from 'rxjs';

import { MessageService, type ChatStreamEvent } from '../../core/services/message.service';
import { SpeechService, markdownToSpeechText } from '../../core/services/speech.service';
import { AppStateService } from '../../core/state/app-state.service';
import { ThemeService } from '../../core/state/theme.service';
import { MarkdownMessageComponent } from '../../shared/components/markdown-message/markdown-message.component';
import type { AppliedMemoryChangeSummary, ContextLoadProfile, Message } from '../../core/models';

export interface StreamToolIndicator {
  id: string;
  label: string;
  status: 'running' | 'success' | 'failed';
}

const profileOptions: ReadonlyArray<{ value: ContextLoadProfile; label: string }> = [
  { value: 'Minimal', label: 'Mínimo' },
  { value: 'Standard', label: 'Estándar' },
  { value: 'Evaluation', label: 'Evaluación' },
  { value: 'Project', label: 'Proyecto' },
  { value: 'FullReview', label: 'Revisión completa' },
];
const pageSize = 50;

@Component({
  selector: 'app-chat',
  imports: [FormsModule, MarkdownMessageComponent],
  templateUrl: './chat.component.html',
  styleUrl: './chat.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ChatComponent implements AfterViewInit, OnDestroy {
  private readonly messageService = inject(MessageService);
  private readonly destroyRef = inject(DestroyRef);
  private intersectionObserver: IntersectionObserver | null = null;
  private topSentinel: ElementRef<HTMLElement> | null = null;
  private currentLoadedSessionId: number | null = null;
  private abortController: AbortController | null = null;
  private copyMessageTimer: ReturnType<typeof setTimeout> | null = null;
  private recognitionInstance: any = null;

  readonly appState = inject(AppStateService);
  readonly themeService = inject(ThemeService);
  readonly speechService = inject(SpeechService);
  readonly isRecording = signal(false);
  readonly recognitionSupported =
    typeof window !== 'undefined' &&
    ('SpeechRecognition' in window || 'webkitSpeechRecognition' in window);
  readonly profileOptions = profileOptions;
  readonly messages = signal<Message[]>([]);
  readonly appliedChangesByMessageId = signal<Record<number, AppliedMemoryChangeSummary[]>>({});
  readonly isLoadingInitial = signal(false);
  readonly isLoadingMore = signal(false);
  readonly hasMore = signal(true);
  readonly sending = signal(false);
  readonly streamingContent = signal('');
  readonly activeToolActivities = signal<StreamToolIndicator[]>([]);
  readonly error = signal<string | null>(null);
  readonly draft = signal('');
  readonly page = signal(1);
  readonly totalCount = signal(0);
  readonly stickToBottom = signal(true);
  readonly hasUnreadBelow = signal(false);
  readonly copiedMessageId = signal<number | null>(null);
  readonly draftTokens = computed(() => this.estimateTokens(this.draft()));
  readonly messageTokens = computed(() =>
    this.messages().reduce((sum, m) => sum + this.estimateTokens(m.content), 0),
  );
  readonly showScrollToBottom = computed(
    () => !this.stickToBottom() && (this.sending() || this.hasUnreadBelow() || this.messages().length > 4),
  );
  readonly unansweredUserMessageId = computed(() => {
    if (this.sending() || this.isLoadingInitial()) {
      return null;
    }

    const list = this.messages();
    const last = list[list.length - 1];
    return last && last.role === 'user' && last.id > 0 ? last.id : null;
  });

  @ViewChild('messagesScroll') private messagesScroll?: ElementRef<HTMLElement>;
  @ViewChild('composerInput') private composerInput?: ElementRef<HTMLTextAreaElement>;

  @ViewChild('topSentinel')
  set topSentinelRef(sentinel: ElementRef<HTMLElement> | undefined) {
    this.topSentinel = sentinel ?? null;
    this.observeTopSentinel();
  }

  constructor() {
    effect(() => {
      const session = this.appState.activeSession();
      const sessionId = session?.id ?? null;

      if (!sessionId) {
        this.currentLoadedSessionId = null;
        this.resetMessagesState();
        return;
      }

      if (this.currentLoadedSessionId === sessionId) {
        return;
      }

      this.currentLoadedSessionId = sessionId;
      this.resetMessagesState();
      this.loadInitialMessages(sessionId);
    });
  }

  ngAfterViewInit(): void {
    this.observeTopSentinel();
  }

  ngOnDestroy(): void {
    this.abortController?.abort();
    this.intersectionObserver?.disconnect();
    if (this.copyMessageTimer) {
      clearTimeout(this.copyMessageTimer);
    }
    this.speechService.stop();
    this.stopDictation();
  }

  toggleReadMessage(message: Message): void {
    const text = markdownToSpeechText(message.content);
    const tutorName = this.appState.activeTutor()?.name.toLowerCase() ?? '';
    const lang = tutorName.includes('inglés') || tutorName.includes('english') ? 'en-US' : 'es-MX';
    this.speechService.toggle(`msg-${message.id}`, text, { lang });
  }

  toggleDictation(): void {
    if (this.isRecording()) {
      this.stopDictation();
      return;
    }
    this.startDictation();
  }

  startDictation(): void {
    const win = globalThis as unknown as {
      SpeechRecognition?: new () => any;
      webkitSpeechRecognition?: new () => any;
    };
    const SpeechRecognitionClass = win.SpeechRecognition || win.webkitSpeechRecognition;
    if (!SpeechRecognitionClass) {
      return;
    }

    try {
      const recognition = new SpeechRecognitionClass();
      recognition.continuous = false;
      recognition.interimResults = false;
      const tutorName = this.appState.activeTutor()?.name.toLowerCase() ?? '';
      recognition.lang = tutorName.includes('inglés') || tutorName.includes('english') ? 'en-US' : 'es-MX';

      recognition.onresult = (event: any) => {
        const transcript = event.results?.[0]?.[0]?.transcript;
        if (transcript) {
          const current = this.draft();
          this.draft.set(current ? `${current} ${transcript}` : transcript);
        }
      };

      recognition.onerror = () => {
        this.isRecording.set(false);
      };

      recognition.onend = () => {
        this.isRecording.set(false);
      };

      this.recognitionInstance = recognition;
      recognition.start();
      this.isRecording.set(true);
    } catch {
      this.isRecording.set(false);
    }
  }

  stopDictation(): void {
    try {
      this.recognitionInstance?.stop();
    } catch {
      // ignore
    }
    this.isRecording.set(false);
  }

  appliedChangesFor(messageId: number): AppliedMemoryChangeSummary[] {
    return this.appliedChangesByMessageId()[messageId] ?? [];
  }

  selectProfile(profile: ContextLoadProfile): void {
    this.appState.selectProfile(profile);
  }

  onMessagesScroll(): void {
    const el = this.messagesScroll?.nativeElement;
    if (!el) {
      return;
    }

    const distanceFromBottom = el.scrollHeight - el.scrollTop - el.clientHeight;
    const nearBottom = distanceFromBottom < 96;
    this.stickToBottom.set(nearBottom);
    if (nearBottom) {
      this.hasUnreadBelow.set(false);
    }
  }

  jumpToBottom(): void {
    this.stickToBottom.set(true);
    this.hasUnreadBelow.set(false);
    this.scrollToBottom();
  }

  openMemoryInspection(): void {
    this.appState.openDrawer('audit');
  }

  requestNewSessionForActiveTutor(): void {
    const tutor = this.appState.activeTutor();
    if (tutor) {
      this.appState.requestCreateSession(tutor.id);
    }
  }

  requestNewTutor(): void {
    this.appState.requestCreateTutor();
  }

  formatMessageTime(iso: string): string {
    if (!iso) {
      return '';
    }
    try {
      const date = new Date(iso);
      if (Number.isNaN(date.getTime())) {
        return '';
      }
      return date.toLocaleTimeString('es-MX', {
        hour: '2-digit',
        minute: '2-digit',
      });
    } catch {
      return '';
    }
  }

  async copyMessage(message: Message): Promise<void> {
    try {
      await globalThis.navigator?.clipboard?.writeText(message.content);
      this.copiedMessageId.set(message.id);
      if (this.copyMessageTimer) {
        clearTimeout(this.copyMessageTimer);
      }
      this.copyMessageTimer = setTimeout(() => {
        if (this.copiedMessageId() === message.id) {
          this.copiedMessageId.set(null);
        }
      }, 2000);
    } catch {
      // Ignore clipboard failure in non-secure environments
    }
  }

  updateDraft(content: string): void {
    this.draft.set(content);
    this.syncComposerHeight();
  }

  handleMessageKeydown(event: KeyboardEvent): void {
    if (event.key !== 'Enter' || event.shiftKey) {
      return;
    }

    event.preventDefault();
    this.sendMessage();
  }

  stopStreaming(): void {
    this.abortController?.abort();
    this.abortController = null;
    this.sending.set(false);
    this.streamingContent.set('');
    this.activeToolActivities.set([]);
  }

  sendMessage(): void {
    const session = this.appState.activeSession();
    const content = this.draft().trim();

    if (!session || !content || this.sending()) {
      return;
    }

    const pendingUserMessage: Message = {
      id: -Date.now(),
      sessionId: session.id,
      role: 'user',
      content,
      createdAtUtc: new Date().toISOString(),
    };

    this.messages.update((messages) => [...messages, pendingUserMessage]);
    this.draft.set('');
    this.resetComposerHeight();
    this.stickToBottom.set(true);
    this.hasUnreadBelow.set(false);
    this.scrollToBottom();

    void this.executeStreamTurn(session.id, content, pendingUserMessage.id, null);
  }

  retryUserMessage(userMessageId: number): void {
    const session = this.appState.activeSession();
    if (!session || this.sending()) {
      return;
    }

    this.stickToBottom.set(true);
    this.hasUnreadBelow.set(false);
    void this.executeStreamTurn(session.id, null, null, userMessageId);
  }

  handleBannerRetry(): void {
    const retryId = this.unansweredUserMessageId();
    if (retryId) {
      this.retryUserMessage(retryId);
      return;
    }

    this.retryLoad();
  }

  retryLoad(): void {
    const session = this.appState.activeSession();

    if (session) {
      this.currentLoadedSessionId = session.id;
      this.resetMessagesState();
      this.loadInitialMessages(session.id);
    }
  }

  loadMoreMessages(): void {
    const session = this.appState.activeSession();

    if (!session || !this.hasMore() || this.isLoadingInitial() || this.isLoadingMore()) {
      return;
    }

    this.loadOlderMessages(session.id);
  }

  private syncComposerHeight(): void {
    const el = this.composerInput?.nativeElement;
    if (!el) {
      return;
    }
    el.style.height = 'auto';
    el.style.height = `${Math.min(el.scrollHeight, 160)}px`;
  }

  private resetComposerHeight(): void {
    const el = this.composerInput?.nativeElement;
    if (el) {
      el.style.height = 'auto';
    }
  }

  private async executeStreamTurn(
    sessionId: number,
    content: string | null,
    pendingTempId: number | null,
    retryUserMessageId: number | null,
  ): Promise<void> {
    this.error.set(null);
    this.sending.set(true);
    this.streamingContent.set('');
    this.activeToolActivities.set([]);

    const controller = new AbortController();
    this.abortController = controller;
    let persistedUserConfirmed = retryUserMessageId !== null;

    try {
      await this.messageService.streamMessage(
        sessionId,
        content,
        this.appState.activeProfile(),
        (event) => {
          if (this.appState.activeSession()?.id !== sessionId) {
            return;
          }
          if (event.type === 'user_message') {
            persistedUserConfirmed = true;
          }
          this.handleStreamEvent(event, pendingTempId);
        },
        controller.signal,
        retryUserMessageId,
      );
    } catch (err: unknown) {
      if (err instanceof DOMException && err.name === 'AbortError') {
        return;
      }

      if (!persistedUserConfirmed && pendingTempId !== null && content !== null) {
        this.messages.update((messages) => messages.filter((m) => m.id !== pendingTempId));
        this.draft.set(content);
        window.setTimeout(() => this.syncComposerHeight());
      }

      this.error.set('No se pudo conectar con el backend. Revisa que esté corriendo e intenta de nuevo.');
    } finally {
      if (this.abortController === controller) {
        this.abortController = null;
        this.sending.set(false);
        this.streamingContent.set('');
        this.activeToolActivities.set([]);
      }
    }
  }

  private handleStreamEvent(event: ChatStreamEvent, pendingTempId: number | null): void {
    switch (event.type) {
      case 'user_message': {
        if (pendingTempId !== null) {
          this.messages.update((messages) =>
            messages.map((m) => (m.id === pendingTempId ? event.data : m)),
          );
        }
        this.appState.touchActiveSessionActivity(event.data.createdAtUtc);
        break;
      }
      case 'tool_call_started': {
        const indicatorId = `${event.data.iteration}-${event.data.toolName}-${event.data.key ?? ''}`;
        const label = this.describeToolActivity(
          event.data.toolName,
          event.data.key,
          event.data.operation,
          'running',
        );
        this.activeToolActivities.update((items) => [
          ...items,
          { id: indicatorId, label, status: 'running' },
        ]);
        this.scrollToBottomIfPinned();
        break;
      }
      case 'tool_call_completed': {
        const status: 'success' | 'failed' = event.data.success ? 'success' : 'failed';
        const label = this.describeToolActivity(
          event.data.toolName,
          event.data.key,
          event.data.operation,
          status,
        );
        this.activeToolActivities.update((items) => {
          const idx = [...items]
            .reverse()
            .findIndex(
              (item) =>
                item.status === 'running' &&
                item.id.startsWith(`${event.data.iteration}-${event.data.toolName}-`),
            );
          if (idx < 0) {
            return items;
          }
          const realIdx = items.length - 1 - idx;
          return items.map((item, i) => (i === realIdx ? { ...item, label, status } : item));
        });
        break;
      }
      case 'token_delta': {
        this.streamingContent.update((prev) => prev + event.data.delta);
        this.scrollToBottomIfPinned();
        break;
      }
      case 'turn_completed': {
        const { userMessage, assistantMessage, appliedMemoryChanges } = event.data;
        this.messages.update((messages) => {
          const withConfirmedUser =
            pendingTempId !== null
              ? messages.map((m) => (m.id === pendingTempId ? userMessage : m))
              : messages;
          return [...withConfirmedUser, assistantMessage];
        });

        if (appliedMemoryChanges?.length > 0) {
          this.appliedChangesByMessageId.update((prev) => ({
            ...prev,
            [assistantMessage.id]: appliedMemoryChanges,
          }));
        }

        this.streamingContent.set('');
        this.activeToolActivities.set([]);
        this.appState.touchActiveSessionActivity(assistantMessage.createdAtUtc);
        this.appState.bumpMemoryVersion();
        this.scrollToBottomIfPinned();
        break;
      }
      case 'error': {
        if (event.data.userMessage && pendingTempId !== null) {
          const persisted = event.data.userMessage;
          this.messages.update((messages) =>
            messages.map((m) => (m.id === pendingTempId ? persisted : m)),
          );
          this.appState.touchActiveSessionActivity(persisted.createdAtUtc);
        }
        if ((event.data.appliedMemoryChanges?.length ?? 0) > 0) {
          this.appState.bumpMemoryVersion();
        }
        this.error.set(
          this.formatStreamErrorMessage(event.data.statusCode, event.data.message),
        );
        break;
      }
    }
  }

  private describeToolActivity(
    toolName: string,
    key?: string | null,
    operation?: string | null,
    status: 'running' | 'success' | 'failed' = 'running',
  ): string {
    const keyLabel = key || 'memoria';
    if (toolName === 'leer_memoria') {
      if (status === 'running') return `Consultando ${keyLabel}...`;
      return status === 'success' ? `Consultado ${keyLabel}` : `Aviso al leer ${keyLabel}`;
    }

    if (toolName === 'guardar_memoria') {
      const opLabel = operation ? ` (${operation})` : '';
      if (status === 'running') return `Actualizando ${keyLabel}${opLabel}...`;
      return status === 'success'
        ? `Memoria ${keyLabel} actualizada`
        : `Ajustando formato en ${keyLabel}`;
    }

    if (toolName === 'listar_memoria') {
      return status === 'running' ? 'Listando memorias del tutor...' : 'Memorias listadas';
    }

    return `${toolName}...`;
  }

  private formatStreamErrorMessage(statusCode: number, detail: string): string {
    if (statusCode === 429 || statusCode === 503) {
      return 'El tutor está ocupado o en límite de cuota temporalmente. Tu mensaje está guardado; pulsa Reintentar en unos segundos.';
    }

    if (statusCode === 404 && detail === 'Not found') {
      this.appState.clearActiveSelection();
      return 'El tutor o la sesión ya no existen. Vuelve a seleccionar una sesión.';
    }

    if (detail?.includes('Request too large')) {
      const totalTokens = this.estimateTotalContextTokens();
      return (
        `El proveedor de IA rechazó la solicitud por exceder el límite de tokens ` +
        `(aprox. ${totalTokens} tokens estimados en el historial + mensaje). ` +
        `Prueba a crear una sesión nueva para reducir el contexto o espera un minuto.`
      );
    }

    return detail || 'El tutor no pudo completar la respuesta. Puedes reintentar sin duplicar tu mensaje.';
  }

  private loadInitialMessages(sessionId: number): void {
    this.isLoadingInitial.set(true);
    this.error.set(null);

    this.messageService
      .getSessionMessages(sessionId, 1, pageSize)
      .pipe(
        finalize(() => this.isLoadingInitial.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (result) => {
          if (this.appState.activeSession()?.id === sessionId) {
            this.page.set(result.page);
            this.totalCount.set(result.totalCount);
            this.hasMore.set(result.page * result.pageSize < result.totalCount);
            this.messages.set([...result.items].reverse());
            this.stickToBottom.set(true);
            this.hasUnreadBelow.set(false);
            this.scrollToBottom();
          }
        },
        error: (error: unknown) => {
          if (this.appState.activeSession()?.id !== sessionId) {
            return;
          }

          this.messages.set([]);
          this.error.set(this.toUserMessage(error));
        },
      });
  }

  private loadOlderMessages(sessionId: number): void {
    const nextPage = this.page() + 1;
    const scrollElement = this.messagesScroll?.nativeElement;
    const previousScrollHeight = scrollElement?.scrollHeight ?? 0;
    const previousScrollTop = scrollElement?.scrollTop ?? 0;

    this.isLoadingMore.set(true);
    this.error.set(null);

    this.messageService
      .getSessionMessages(sessionId, nextPage, pageSize)
      .pipe(
        finalize(() => this.isLoadingMore.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (result) => {
          if (this.appState.activeSession()?.id !== sessionId) {
            return;
          }

          const olderMessages = [...result.items].reverse();
          this.page.set(result.page);
          this.totalCount.set(result.totalCount);
          this.hasMore.set(result.page * result.pageSize < result.totalCount);
          this.messages.update((messages) => [...olderMessages, ...messages]);

          window.setTimeout(() => {
            const currentScrollElement = this.messagesScroll?.nativeElement;

            if (!currentScrollElement) {
              return;
            }

            currentScrollElement.scrollTop =
              currentScrollElement.scrollHeight - previousScrollHeight + previousScrollTop;
          });
        },
        error: (error: unknown) => {
          if (this.appState.activeSession()?.id !== sessionId) {
            return;
          }

          this.error.set(this.toUserMessage(error));
        },
      });
  }

  private resetMessagesState(): void {
    this.abortController?.abort();
    this.abortController = null;
    this.sending.set(false);
    this.streamingContent.set('');
    this.activeToolActivities.set([]);
    this.messages.set([]);
    this.appliedChangesByMessageId.set({});
    this.page.set(1);
    this.totalCount.set(0);
    this.hasMore.set(true);
    this.isLoadingInitial.set(false);
    this.isLoadingMore.set(false);
    this.stickToBottom.set(true);
    this.hasUnreadBelow.set(false);
    this.error.set(null);
  }

  private observeTopSentinel(): void {
    this.intersectionObserver?.disconnect();

    const sentinel = this.topSentinel?.nativeElement;
    const root = this.messagesScroll?.nativeElement;

    if (!sentinel || !root) {
      return;
    }

    this.intersectionObserver = new IntersectionObserver(
      ([entry]) => {
        if (entry.isIntersecting) {
          this.loadMoreMessages();
        }
      },
      { root, threshold: 0.1 },
    );

    this.intersectionObserver.observe(sentinel);
  }

  private scrollToBottomIfPinned(): void {
    if (this.stickToBottom()) {
      this.scrollToBottom();
    } else {
      this.hasUnreadBelow.set(true);
    }
  }

  private scrollToBottom(): void {
    window.setTimeout(() => {
      const scrollElement = this.messagesScroll?.nativeElement;

      if (scrollElement) {
        scrollElement.scrollTop = scrollElement.scrollHeight;
      }
    });
  }

  private toUserMessage(error: unknown): string {
    if (!(error instanceof HttpErrorResponse)) {
      return 'Ocurrió un error inesperado. Intenta de nuevo.';
    }

    if (error.status === 404) {
      this.appState.clearActiveSelection();
      return 'El tutor o la sesión ya no existen. Vuelve a seleccionar una sesión.';
    }

    if (error.status === 0 || error.status >= 500) {
      return 'No se pudo conectar con el backend. Revisa que esté corriendo e intenta de nuevo.';
    }

    return 'No se pudo completar la operación. Intenta de nuevo.';
  }

  private estimateTokens(text: string): number {
    return Math.ceil(text.length / 4);
  }

  private estimateTotalContextTokens(): number {
    const sessionTokens = this.messages().reduce(
      (sum, message) => sum + this.estimateTokens(message.content),
      0,
    );
    const draftTokens = this.estimateTokens(this.draft());
    return sessionTokens + draftTokens;
  }
}
