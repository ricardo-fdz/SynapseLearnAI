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

import { MessageService } from '../../core/services/message.service';
import { AppStateService } from '../../core/state/app-state.service';
import { MarkdownMessageComponent } from '../../shared/components/markdown-message/markdown-message.component';
import type { ContextLoadProfile, Message } from '../../core/models';

const profiles: ContextLoadProfile[] = ['Minimal', 'Standard', 'Evaluation', 'Project', 'FullReview'];
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
  readonly appState = inject(AppStateService);
  readonly profiles = profiles;
  readonly messages = signal<Message[]>([]);
  readonly isLoadingInitial = signal(false);
  readonly isLoadingMore = signal(false);
  readonly hasMore = signal(true);
  readonly sending = signal(false);
  readonly error = signal<string | null>(null);
  readonly draft = signal('');
  readonly page = signal(1);
  readonly totalCount = signal(0);
  readonly draftTokens = computed(() => this.estimateTokens(this.draft()));
  readonly messageTokens = computed(() =>
    this.messages().reduce((sum, m) => sum + this.estimateTokens(m.content), 0),
  );

  @ViewChild('messagesScroll') private messagesScroll?: ElementRef<HTMLElement>;

  @ViewChild('topSentinel')
  set topSentinelRef(sentinel: ElementRef<HTMLElement> | undefined) {
    this.topSentinel = sentinel ?? null;
    this.observeTopSentinel();
  }

  constructor() {
    effect(() => {
      const session = this.appState.activeSession();

      if (!session) {
        this.resetMessagesState();
        return;
      }

      this.resetMessagesState();
      this.loadInitialMessages(session.id);
    });
  }

  ngAfterViewInit(): void {
    this.observeTopSentinel();
  }

  ngOnDestroy(): void {
    this.intersectionObserver?.disconnect();
  }

  selectProfile(profile: ContextLoadProfile): void {
    this.appState.selectProfile(profile);
  }

  updateDraft(content: string): void {
    this.draft.set(content);
  }

  handleMessageKeydown(event: KeyboardEvent): void {
    if (event.key !== 'Enter' || event.shiftKey) {
      return;
    }

    event.preventDefault();
    this.sendMessage();
  }

  sendMessage(): void {
    const session = this.appState.activeSession();
    const content = this.draft().trim();

    if (!session || !content || this.sending()) {
      return;
    }

    this.error.set(null);
    this.sending.set(true);

    const pendingUserMessage: Message = {
      id: -Date.now(),
      sessionId: session.id,
      role: 'user',
      content,
      createdAtUtc: new Date().toISOString(),
    };

    this.messages.update((messages) => [...messages, pendingUserMessage]);
    this.draft.set('');

    this.messageService
      .sendMessage(session.id, content, this.appState.activeProfile())
      .pipe(
        finalize(() => this.sending.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (response) => {
          if (this.appState.activeSession()?.id !== session.id) {
            return;
          }

          this.messages.update((messages) => [...messages, response.assistantMessage]);
          this.scrollToBottom();
        },
        error: (error: unknown) => {
          this.messages.update((messages) =>
            messages.filter((message) => message.id !== pendingUserMessage.id),
          );
          this.error.set(this.toUserMessage(error));
          this.draft.set(content);
        },
      });
  }

  retryLoad(): void {
    const session = this.appState.activeSession();

    if (session) {
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
    this.messages.set([]);
    this.page.set(1);
    this.totalCount.set(0);
    this.hasMore.set(true);
    this.isLoadingInitial.set(false);
    this.isLoadingMore.set(false);
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

    if (error.status === 429 || error.status === 503) {
      return 'El tutor está ocupado en este momento, intenta en unos segundos.';
    }

    if (error.status === 404) {
      this.appState.clearActiveSelection();
      return 'El tutor o la sesión ya no existen. Vuelve a seleccionar una sesión.';
    }

    if (error.status === 502) {
      const detail = error.error?.detail;
      const totalTokens = this.estimateTotalContextTokens();

      if (detail && detail.includes('Request too large')) {
        return (
          `El proveedor de IA rechazó la solicitud por exceder el límite de tokens ` +
          `(aprox. ${totalTokens} tokens estimados en el historial + mensaje). ` +
          `Prueba a crear una sesión nueva para reducir el contexto o espera un minuto.`
        );
      }

      return `El tutor no está disponible en este momento (502 Bad Gateway). Intenta de nuevo.`;
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
