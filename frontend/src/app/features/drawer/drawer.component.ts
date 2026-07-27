import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  EventEmitter,
  Input,
  Output,
  effect,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize, forkJoin } from 'rxjs';

import { AuditService } from '../../core/services/audit.service';
import { MemoryService } from '../../core/services/memory.service';
import { TutorService } from '../../core/services/tutor.service';
import { AppStateService } from '../../core/state/app-state.service';
import type { ContextLoadProfile, MemoryChange, MemoryEntry, PagedResult } from '../../core/models';

type DrawerTab = 'memory' | 'audit' | 'prompt';

@Component({
  selector: 'app-drawer',
  templateUrl: './drawer.component.html',
  styleUrl: './drawer.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DrawerComponent {
  @Input() showCloseButton = false;
  @Output() closeRequested = new EventEmitter<void>();

  private readonly memoryService = inject(MemoryService);
  private readonly auditService = inject(AuditService);
  private readonly tutorService = inject(TutorService);
  private readonly destroyRef = inject(DestroyRef);
  readonly appState = inject(AppStateService);

  readonly activeTab = signal<DrawerTab>('memory');
  readonly memoryEntries = signal<MemoryEntry[]>([]);
  readonly audit = signal<PagedResult<MemoryChange> | null>(null);
  readonly promptPreview = signal('');
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly auditPage = signal(1);
  readonly auditPageSize = 20;

  constructor() {
    effect(() => {
      const tutor = this.appState.activeTutor();
      const profile = this.appState.activeProfile();

      if (!tutor) {
        this.memoryEntries.set([]);
        this.audit.set(null);
        this.promptPreview.set('');
        this.error.set(null);
        return;
      }

      this.loadTutorDiagnostics(tutor.id, profile, 1);
    });
  }

  selectTab(tab: DrawerTab): void {
    this.activeTab.set(tab);
  }

  requestClose(): void {
    this.closeRequested.emit();
  }

  nextAuditPage(): void {
    const audit = this.audit();

    if (!audit || audit.page * audit.pageSize >= audit.totalCount) {
      return;
    }

    this.loadAuditPage(audit.page + 1);
  }

  previousAuditPage(): void {
    const audit = this.audit();

    if (!audit || audit.page <= 1) {
      return;
    }

    this.loadAuditPage(audit.page - 1);
  }

  reload(): void {
    const tutor = this.appState.activeTutor();

    if (tutor) {
      this.loadTutorDiagnostics(tutor.id, this.appState.activeProfile(), this.auditPage());
    }
  }

  formatDate(iso: string): string {
    try {
      const date = new Date(iso);
      return date.toLocaleString('es-MX', {
        year: 'numeric',
        month: 'long',
        day: 'numeric',
        hour: '2-digit',
        minute: '2-digit',
        second: '2-digit',
      });
    } catch {
      return iso;
    }
  }

  formatMemoryValue(valueJson: string): string {
    try {
      return JSON.stringify(JSON.parse(valueJson), null, 2);
    } catch {
      return valueJson;
    }
  }

  private loadTutorDiagnostics(tutorId: number, profile: ContextLoadProfile, page: number): void {
    this.loading.set(true);
    this.error.set(null);
    this.auditPage.set(page);

    forkJoin({
      memoryEntries: this.memoryService.getMemoryEntriesByTutor(tutorId),
      audit: this.auditService.getTutorMemoryChanges(tutorId, page, this.auditPageSize),
      promptPreview: this.tutorService.getPromptPreview(tutorId, profile),
    })
      .pipe(
        finalize(() => this.loading.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: ({ memoryEntries, audit, promptPreview }) => {
          if (this.appState.activeTutor()?.id !== tutorId) {
            return;
          }

          this.memoryEntries.set(memoryEntries);
          this.audit.set(audit);
          this.promptPreview.set(promptPreview);
        },
        error: () => this.error.set('No se pudo cargar el diagnóstico del tutor.'),
      });
  }

  private loadAuditPage(page: number): void {
    const tutor = this.appState.activeTutor();

    if (!tutor) {
      return;
    }

    this.loading.set(true);
    this.error.set(null);
    this.auditPage.set(page);

    this.auditService
      .getTutorMemoryChanges(tutor.id, page, this.auditPageSize)
      .pipe(
        finalize(() => this.loading.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (audit) => this.audit.set(audit),
        error: () => this.error.set('No se pudo cargar la auditoría.'),
      });
  }
}
