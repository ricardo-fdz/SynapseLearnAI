import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  EventEmitter,
  Input,
  Output,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize, forkJoin } from 'rxjs';

import { AuditService } from '../../core/services/audit.service';
import { MemoryService } from '../../core/services/memory.service';
import { TutorService } from '../../core/services/tutor.service';
import { AppStateService, type DiagnosticsDrawerTab } from '../../core/state/app-state.service';
import { ModalComponent } from '../../shared/components/modal/modal.component';
import type { ContextLoadProfile, MemoryChange, MemoryEntry, PagedResult } from '../../core/models';

type MemoryViewMode = 'structured' | 'json';

export interface DomainTopicView {
  id: string;
  nombre: string;
  nivelNum: number;
  nivelLabel: string;
  notas?: string;
  ultimaEvaluacion?: string;
}

export interface GapItemView {
  id: string;
  concepto: string;
  descripcion: string;
  vecesVisto: number;
  comoSeResolvio?: string;
  fechaResolucion?: string;
}

export interface RoadmapStepView {
  orden: string;
  ref: string;
  saltable: boolean;
}

export interface RoadmapItemView {
  id: string;
  titulo: string;
  obligatorio: boolean;
  temas: RoadmapStepView[];
}

export interface KeyValuePairView {
  label: string;
  value: string;
}

export interface ActivityItemView {
  nombre: string;
  resultado: string;
  fecha?: string;
}

const profileLabels: Record<ContextLoadProfile, string> = {
  Minimal: 'Mínimo',
  Standard: 'Estándar',
  Evaluation: 'Evaluación',
  Project: 'Proyecto',
  FullReview: 'Revisión completa',
};

@Component({
  selector: 'app-drawer',
  imports: [ModalComponent],
  templateUrl: './drawer.component.html',
  styleUrl: './drawer.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DrawerComponent {
  @Input() showCloseButton = true;
  @Output() closeRequested = new EventEmitter<void>();

  private readonly memoryService = inject(MemoryService);
  private readonly auditService = inject(AuditService);
  private readonly tutorService = inject(TutorService);
  private readonly destroyRef = inject(DestroyRef);
  readonly appState = inject(AppStateService);

  readonly activeTab = this.appState.drawerTab;
  readonly memoryViewMode = signal<MemoryViewMode>('structured');
  readonly memoryEntries = signal<MemoryEntry[]>([]);
  readonly audit = signal<PagedResult<MemoryChange> | null>(null);
  readonly promptPreview = signal('');
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly auditPage = signal(1);
  readonly auditPageSize = 20;
  readonly expandedDiffIds = signal<ReadonlySet<number>>(new Set());
  readonly rollingBackChangeId = signal<number | null>(null);
  readonly pendingRollbackChange = signal<MemoryChange | null>(null);
  readonly hasLoadedData = computed(
    () => this.memoryEntries().length > 0 || this.audit() !== null || this.promptPreview().length > 0,
  );

  constructor() {
    effect(() => {
      const tutor = this.appState.activeTutor();
      const profile = this.appState.activeProfile();
      const sessionId = this.appState.activeSession()?.id ?? null;
      this.appState.memoryVersion();

      if (!tutor) {
        this.memoryEntries.set([]);
        this.audit.set(null);
        this.promptPreview.set('');
        this.error.set(null);
        return;
      }

      this.loadTutorDiagnostics(tutor.id, profile, 1, sessionId);
    });
  }

  profileLabel(profile: ContextLoadProfile): string {
    return profileLabels[profile] ?? profile;
  }

  selectTab(tab: DiagnosticsDrawerTab): void {
    this.appState.setDrawerTab(tab);
  }

  setMemoryViewMode(mode: MemoryViewMode): void {
    this.memoryViewMode.set(mode);
  }

  toggleDiff(changeId: number): void {
    this.expandedDiffIds.update((set) => {
      const next = new Set(set);
      if (next.has(changeId)) {
        next.delete(changeId);
      } else {
        next.add(changeId);
      }
      return next;
    });
  }

  isDiffExpanded(changeId: number): boolean {
    return this.expandedDiffIds().has(changeId);
  }

  requestRollbackChange(change: MemoryChange): void {
    if (!this.appState.activeTutor() || this.rollingBackChangeId() !== null) {
      return;
    }
    this.pendingRollbackChange.set(change);
  }

  cancelRollbackChange(): void {
    this.pendingRollbackChange.set(null);
  }

  confirmRollbackChange(): void {
    const change = this.pendingRollbackChange();
    const tutor = this.appState.activeTutor();
    if (!change || !tutor || this.rollingBackChangeId() !== null) {
      return;
    }

    this.pendingRollbackChange.set(null);
    this.rollingBackChangeId.set(change.id);
    this.error.set(null);

    this.auditService
      .rollbackMemoryChange(tutor.id, change.id)
      .pipe(
        finalize(() => this.rollingBackChangeId.set(null)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: () => {
          this.appState.bumpMemoryVersion();
        },
        error: () => {
          this.error.set('No se pudo revertir el cambio de memoria.');
        },
      });
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
      this.loadTutorDiagnostics(
        tutor.id,
        this.appState.activeProfile(),
        this.auditPage(),
        this.appState.activeSession()?.id ?? null,
      );
    }
  }

  formatDate(iso: string): string {
    try {
      const date = new Date(iso);
      return date.toLocaleString('es-MX', {
        year: 'numeric',
        month: 'short',
        day: 'numeric',
        hour: '2-digit',
        minute: '2-digit',
      });
    } catch {
      return iso;
    }
  }

  formatMemoryValue(valueJson?: string): string {
    if (!valueJson) {
      return '{}';
    }
    try {
      return JSON.stringify(JSON.parse(valueJson), null, 2);
    } catch {
      return valueJson;
    }
  }

  memoryTitle(key: string): string {
    switch (key) {
      case 'perfil_estudiante':
        return 'Perfil del estudiante';
      case 'memoria_sesion':
        return 'Estado de sesión';
      case 'mapa_dominio':
        return 'Mapa de dominio';
      case 'lagunas_o_errores':
        return 'Lagunas y errores';
      case 'historial_actividades':
        return 'Historial de actividades';
      case 'roadmap':
        return 'Roadmap';
      default:
        return key;
    }
  }

  parseDomainTopics(valueJson: string): DomainTopicView[] {
    const root = this.safeParseObject(valueJson);
    if (!root) {
      return [];
    }

    const rawList = Array.isArray(root['temas'])
      ? root['temas']
      : Array.isArray(root['habilidades'])
        ? root['habilidades']
        : [];

    return rawList
      .filter((item): item is Record<string, unknown> => !!item && typeof item === 'object')
      .map((item) => {
        const rawNivel = item['nivel'] ?? item['dominio'] ?? 0;
        const parsedNum =
          typeof rawNivel === 'number'
            ? Math.max(0, Math.min(5, rawNivel))
            : Number.parseInt(String(rawNivel), 10) || 0;
        return {
          id: String(item['id'] ?? ''),
          nombre: String(item['nombre'] ?? item['tema'] ?? item['habilidad'] ?? 'Tema'),
          nivelNum: Math.max(0, Math.min(5, parsedNum)),
          nivelLabel: String(rawNivel || '0'),
          notas: item['notas'] ? String(item['notas']) : undefined,
          ultimaEvaluacion: item['ultima_evaluacion'] ? String(item['ultima_evaluacion']) : undefined,
        };
      });
  }

  parseGaps(valueJson: string): { activas: GapItemView[]; resueltas: GapItemView[] } {
    const root = this.safeParseObject(valueJson);
    if (!root) {
      return { activas: [], resueltas: [] };
    }

    const mapGap = (raw: unknown): GapItemView | null => {
      if (!raw || typeof raw !== 'object') {
        return null;
      }
      const item = raw as Record<string, unknown>;
      return {
        id: String(item['id'] ?? ''),
        concepto: String(item['concepto'] ?? item['tema'] ?? 'Laguna'),
        descripcion: String(item['descripcion'] ?? ''),
        vecesVisto: typeof item['veces_visto'] === 'number' ? item['veces_visto'] : 1,
        comoSeResolvio: item['como_se_resolvio'] ? String(item['como_se_resolvio']) : undefined,
        fechaResolucion: item['fecha_resolucion'] ? String(item['fecha_resolucion']) : undefined,
      };
    };

    const activas = Array.isArray(root['activas'])
      ? (root['activas'].map(mapGap).filter(Boolean) as GapItemView[])
      : [];
    const resueltas = Array.isArray(root['resueltas'])
      ? (root['resueltas'].map(mapGap).filter(Boolean) as GapItemView[])
      : [];

    return { activas, resueltas };
  }

  parseRoadmaps(valueJson: string): RoadmapItemView[] {
    const root = this.safeParseObject(valueJson);
    if (!root || !Array.isArray(root['roadmaps'])) {
      return [];
    }

    return root['roadmaps']
      .filter((item): item is Record<string, unknown> => !!item && typeof item === 'object')
      .map((item) => {
        const temas = Array.isArray(item['temas'])
          ? item['temas']
              .filter((t): t is Record<string, unknown> => !!t && typeof t === 'object')
              .map((t) => ({
                orden: String(t['orden'] ?? '?'),
                ref: String(t['ref'] ?? t['id'] ?? 'tema'),
                saltable: Boolean(t['saltable']),
              }))
          : [];
        return {
          id: String(item['id'] ?? ''),
          titulo: String(item['titulo'] ?? item['nombre'] ?? 'Roadmap'),
          obligatorio: Boolean(item['obligatorio']),
          temas,
        };
      });
  }

  parseKeyValueSummary(valueJson: string): KeyValuePairView[] {
    const root = this.safeParseObject(valueJson);
    if (!root) {
      return [];
    }

    const pairs: KeyValuePairView[] = [];
    for (const [key, value] of Object.entries(root)) {
      if (value === null || value === undefined || value === '') {
        continue;
      }
      if (Array.isArray(value) && value.length === 0) {
        continue;
      }
      if (typeof value === 'object' && !Array.isArray(value) && Object.keys(value).length === 0) {
        continue;
      }

      const formatted =
        typeof value === 'string' || typeof value === 'number' || typeof value === 'boolean'
          ? String(value)
          : JSON.stringify(value);
      pairs.push({
        label: key.replace(/_/g, ' '),
        value: formatted,
      });
    }
    return pairs;
  }

  parseActivities(valueJson: string): ActivityItemView[] {
    const root = this.safeParseObject(valueJson);
    if (!root) {
      return [];
    }

    const raw = Array.isArray(root['proyectos'])
      ? root['proyectos']
      : Array.isArray(root['actividades'])
        ? root['actividades']
        : [];

    return raw
      .filter((item): item is Record<string, unknown> => !!item && typeof item === 'object')
      .map((item) => ({
        nombre: String(item['nombre'] ?? item['titulo'] ?? item['actividad'] ?? 'Actividad'),
        resultado: String(item['resultado'] ?? item['notas'] ?? ''),
        fecha: item['fecha'] ? String(item['fecha']) : undefined,
      }));
  }

  private safeParseObject(valueJson: string): Record<string, unknown> | null {
    try {
      const parsed: unknown = JSON.parse(valueJson);
      return parsed && typeof parsed === 'object' && !Array.isArray(parsed)
        ? (parsed as Record<string, unknown>)
        : null;
    } catch {
      return null;
    }
  }

  private loadTutorDiagnostics(
    tutorId: number,
    profile: ContextLoadProfile,
    page: number,
    sessionId: number | null,
  ): void {
    this.loading.set(true);
    this.error.set(null);
    this.auditPage.set(page);

    forkJoin({
      memoryEntries: this.memoryService.getMemoryEntriesByTutor(tutorId),
      audit: this.auditService.getTutorMemoryChanges(tutorId, page, this.auditPageSize),
      promptPreview: this.tutorService.getPromptPreview(tutorId, profile, sessionId),
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
