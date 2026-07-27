import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  EventEmitter,
  Input,
  Output,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { finalize, forkJoin } from 'rxjs';

import { SessionService, type StudySessionRequest } from '../../core/services/session.service';
import {
  TutorService,
  type CreateTutorRequest,
  type InitialStudentProfile,
  type TutorRequest,
} from '../../core/services/tutor.service';
import { AppStateService } from '../../core/state/app-state.service';
import { ModalComponent } from '../../shared/components/modal/modal.component';
import type { StudySession, Tutor } from '../../core/models';

interface TutorForm {
  name: string;
  description: string;
  systemPromptContent: string;
  geminiModel: string;
}

interface SessionForm {
  name: string;
  goal: string;
}

interface StudentProfileForm {
  alias: string;
  lenguajePrincipal: string;
  objetivoDeclarado: string;
  prefiere: string;
  ritmoSesion: string;
  reaccionAnteErrores: string;
  nivelAutonomia: string;
  idioma: string;
  tonoTutor: string;
}

@Component({
  selector: 'app-sidebar',
  imports: [FormsModule, ModalComponent],
  templateUrl: './sidebar.component.html',
  styleUrl: './sidebar.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SidebarComponent {
  @Input() showCloseButton = false;
  @Output() closeRequested = new EventEmitter<void>();
  @Output() sessionSelected = new EventEmitter<void>();

  private readonly tutorService = inject(TutorService);
  private readonly sessionService = inject(SessionService);
  private readonly destroyRef = inject(DestroyRef);
  readonly appState = inject(AppStateService);

  readonly tutors = signal<Tutor[]>([]);
  readonly sessions = signal<StudySession[]>([]);
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly error = signal<string | null>(null);
  readonly showTutorForm = signal(false);
  readonly createTutorStep = signal<1 | 2>(1);
  readonly editingTutorId = signal<number | null>(null);
  readonly sessionFormTutorId = signal<number | null>(null);
  readonly editingSessionId = signal<number | null>(null);
  readonly expandedTutorIds = signal<ReadonlySet<number>>(new Set<number>());
  readonly openMenuId = signal<string | null>(null);
  readonly hasTutors = computed(() => this.tutors().length > 0);
  readonly learningPreferenceOptions = ['analogías', 'ejemplos_directos', 'proyectos', 'combinacion'];
  readonly sessionPaceOptions = ['cortas_intensas', 'largas_progresivas'];
  readonly errorReactionOptions = ['se_frustra_rapido', 'resiliente', 'neutral'];
  readonly autonomyOptions = ['necesita_mucha_guia', 'pistas_minimas', 'muy_autonomo'];
  readonly languageOptions = ['espanol', 'ingles', 'combinacion'];
  readonly toneOptions = ['estricto_directo', 'alentador_paciente', 'neutral'];

  newTutor: TutorForm = this.emptyTutorForm();
  newStudentProfile: StudentProfileForm = this.emptyStudentProfileForm();
  editTutor: TutorForm = this.emptyTutorForm();
  newSession: SessionForm = this.emptySessionForm();
  editSession: SessionForm = this.emptySessionForm();

  constructor() {
    this.loadData();
  }

  sessionsForTutor(tutorId: number): StudySession[] {
    return this.sessions().filter((session) => session.tutorId === tutorId);
  }

  latestSessionLabel(tutorId: number): string {
    const latestSession = this.sessionsForTutor(tutorId)[0];

    if (!latestSession) {
      return 'Sin sesiones';
    }

    return `Última: ${latestSession.name}`;
  }

  isTutorExpanded(tutorId: number): boolean {
    return this.expandedTutorIds().has(tutorId);
  }

  toggleTutor(tutorId: number): void {
    this.closeMenu();

    this.expandedTutorIds.update((expanded) => {
      const next = new Set(expanded);

      if (next.has(tutorId)) {
        next.delete(tutorId);
      } else {
        next.add(tutorId);
      }

      return next;
    });
  }

  toggleMenu(id: string): void {
    this.openMenuId.update((current) => (current === id ? null : id));
  }

  closeMenu(): void {
    this.openMenuId.set(null);
  }

  selectTutor(tutor: Tutor): void {
    this.closeMenu();
    this.appState.selectTutor(tutor);
    this.expandTutor(tutor.id);
  }

  selectSession(tutor: Tutor, session: StudySession): void {
    this.closeMenu();

    if (this.appState.activeTutor()?.id !== tutor.id) {
      this.appState.selectTutor(tutor);
    }

    this.expandTutor(tutor.id);
    this.appState.selectSession(session);
    this.sessionSelected.emit();
  }

  openCreateTutor(): void {
    this.newTutor = this.emptyTutorForm();
    this.newStudentProfile = this.emptyStudentProfileForm();
    this.createTutorStep.set(1);
    this.showTutorForm.set(true);
    this.editingTutorId.set(null);
    this.closeMenu();
  }

  cancelTutorForm(): void {
    this.showTutorForm.set(false);
    this.createTutorStep.set(1);
    this.editingTutorId.set(null);
  }

  goToTutorProfileStep(): void {
    const request = this.toCreateTutorRequest(this.newTutor);

    if (!request.name) {
      this.error.set('El tutor necesita un nombre.');
      return;
    }

    this.error.set(null);
    this.createTutorStep.set(2);
  }

  goToTutorDataStep(): void {
    this.createTutorStep.set(1);
  }

  createTutor(): void {
    const request = this.toCreateTutorRequest(this.newTutor, this.newStudentProfile);

    if (!request.name) {
      this.error.set('El tutor necesita un nombre.');
      return;
    }

    this.saving.set(true);
    this.error.set(null);

    this.tutorService
      .createTutor(request)
      .pipe(
        finalize(() => this.saving.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (tutor) => {
          this.showTutorForm.set(false);
          this.createTutorStep.set(1);
          this.expandTutor(tutor.id);
          this.loadData(tutor.id);
        },
        error: () => this.error.set('No se pudo crear el tutor.'),
      });
  }

  startEditTutor(tutor: Tutor): void {
    this.editTutor = {
      name: tutor.name,
      description: tutor.description,
      systemPromptContent: tutor.systemPromptContent,
      geminiModel: tutor.geminiModel,
    };
    this.editingTutorId.set(tutor.id);
    this.showTutorForm.set(false);
    this.closeMenu();
  }

  cancelEditTutor(): void {
    this.editingTutorId.set(null);
  }

  updateTutor(tutor: Tutor): void {
    const request = this.toTutorRequest(this.editTutor);

    if (!request.name) {
      this.error.set('El tutor necesita un nombre.');
      return;
    }

    this.saving.set(true);
    this.error.set(null);

    this.tutorService
      .updateTutor(tutor.id, request)
      .pipe(
        finalize(() => this.saving.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: () => {
          this.editingTutorId.set(null);
          this.loadData(tutor.id, this.appState.activeSession()?.id ?? null);
        },
        error: () => this.error.set('No se pudo actualizar el tutor.'),
      });
  }

  deleteTutor(tutor: Tutor): void {
    this.closeMenu();

    if (!globalThis.confirm(`Eliminar el tutor "${tutor.name}" y sus sesiones?`)) {
      return;
    }

    this.saving.set(true);
    this.error.set(null);

    this.tutorService
      .deleteTutor(tutor.id)
      .pipe(
        finalize(() => this.saving.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: () => {
          if (this.appState.activeTutor()?.id === tutor.id) {
            this.appState.clearActiveSelection();
          }

          this.loadData();
        },
        error: () => this.error.set('No se pudo eliminar el tutor.'),
      });
  }

  openCreateSession(tutor: Tutor): void {
    this.newSession = this.emptySessionForm();
    this.sessionFormTutorId.set(tutor.id);
    this.editingSessionId.set(null);
    this.expandTutor(tutor.id);
    this.closeMenu();
  }

  cancelSessionForm(): void {
    this.sessionFormTutorId.set(null);
    this.editingSessionId.set(null);
  }

  createSession(tutor: Tutor): void {
    const request = this.toSessionRequest(tutor.id, this.newSession);

    if (!request.name) {
      this.error.set('La sesión necesita un nombre.');
      return;
    }

    this.saving.set(true);
    this.error.set(null);

    this.sessionService
      .createSession(request)
      .pipe(
        finalize(() => this.saving.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (session) => {
          this.sessionFormTutorId.set(null);
          this.appState.selectTutor(tutor);
          this.expandTutor(tutor.id);
          this.loadData(tutor.id, session.id);
        },
        error: () => this.error.set('No se pudo crear la sesión.'),
      });
  }

  startEditSession(session: StudySession): void {
    this.editSession = { name: session.name, goal: session.goal };
    this.editingSessionId.set(session.id);
    this.sessionFormTutorId.set(null);
    this.closeMenu();
  }

  updateSession(session: StudySession): void {
    const request = this.toSessionRequest(session.tutorId, this.editSession);

    if (!request.name) {
      this.error.set('La sesión necesita un nombre.');
      return;
    }

    this.saving.set(true);
    this.error.set(null);

    this.sessionService
      .updateSession(session.id, request)
      .pipe(
        finalize(() => this.saving.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: () => {
          this.editingSessionId.set(null);
          this.loadData(session.tutorId, session.id);
        },
        error: () => this.error.set('No se pudo actualizar la sesión.'),
      });
  }

  deleteSession(session: StudySession): void {
    this.closeMenu();

    if (!globalThis.confirm(`Eliminar la sesión "${session.name}"?`)) {
      return;
    }

    this.saving.set(true);
    this.error.set(null);

    this.sessionService
      .deleteSession(session.id)
      .pipe(
        finalize(() => this.saving.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: () => {
          if (this.appState.activeSession()?.id === session.id) {
            this.appState.selectSession(null);
          }

          this.loadData(this.appState.activeTutor()?.id ?? null);
        },
        error: () => this.error.set('No se pudo eliminar la sesión.'),
      });
  }

  private loadData(selectTutorId: number | null = null, selectSessionId: number | null = null): void {
    this.loading.set(true);
    this.error.set(null);

    forkJoin({
      tutors: this.tutorService.getTutors(),
      sessions: this.sessionService.getSessions(),
    })
      .pipe(
        finalize(() => this.loading.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: ({ tutors, sessions }) => {
          const sortedSessions = this.sortSessions(sessions);
          const sortedTutors = this.sortTutorsBySessionActivity(tutors, sortedSessions);

          this.tutors.set(sortedTutors);
          this.sessions.set(sortedSessions);
          this.syncActiveSelection(tutors, sessions, selectTutorId, selectSessionId);
        },
        error: () => {
          this.tutors.set([]);
          this.sessions.set([]);
          this.error.set('No se pudo conectar con el backend local.');
        },
      });
  }

  private syncActiveSelection(
    tutors: Tutor[],
    sessions: StudySession[],
    selectTutorId: number | null,
    selectSessionId: number | null,
  ): void {
    const activeTutorId = selectTutorId ?? this.appState.activeTutor()?.id ?? null;
    const activeSessionId = selectSessionId ?? this.appState.activeSession()?.id ?? null;
    const activeTutor = tutors.find((tutor) => tutor.id === activeTutorId) ?? null;
    const activeSession = sessions.find((session) => session.id === activeSessionId) ?? null;

    if (!activeTutor && !activeSession && sessions.length > 0) {
      const latestSession = this.sortSessions(sessions)[0];
      const latestTutor = tutors.find((tutor) => tutor.id === latestSession.tutorId) ?? null;

      if (latestTutor) {
        this.appState.selectTutor(latestTutor);
        this.appState.selectSession(latestSession);
        this.expandTutor(latestTutor.id);
      }

      return;
    }

    if (activeTutor) {
      if (selectTutorId !== null || !this.appState.activeTutor()) {
        this.appState.selectTutor(activeTutor);
      } else {
        this.appState.updateActiveTutor(activeTutor);
      }

      this.expandTutor(activeTutor.id);
    }

    if (activeSession) {
      if (selectSessionId !== null || !this.appState.activeSession()) {
        this.appState.selectSession(activeSession);
      } else {
        this.appState.updateActiveSession(activeSession);
      }
    }
  }

  private toTutorRequest(form: TutorForm): TutorRequest {
    return {
      name: form.name.trim(),
      description: form.description.trim(),
      systemPromptContent: form.systemPromptContent.trim(),
      geminiModel: form.geminiModel.trim() || 'gemini-2.0-flash',
    };
  }

  private toCreateTutorRequest(
    tutorForm: TutorForm,
    profileForm?: StudentProfileForm,
  ): CreateTutorRequest {
    const profile = profileForm ? this.toInitialStudentProfile(profileForm) : undefined;

    return {
      name: tutorForm.name.trim(),
      description: tutorForm.description.trim(),
      systemPromptContent: tutorForm.systemPromptContent.trim(),
      ...(profile ? { initialStudentProfile: profile } : {}),
    };
  }

  private toInitialStudentProfile(form: StudentProfileForm): InitialStudentProfile | undefined {
    const alias = form.alias.trim();
    const lenguajePrincipal = form.lenguajePrincipal.trim();
    const objetivoDeclarado = form.objetivoDeclarado.trim();
    const prefiere = form.prefiere.trim();
    const ritmoSesion = form.ritmoSesion.trim();
    const reaccionAnteErrores = form.reaccionAnteErrores.trim();
    const nivelAutonomia = form.nivelAutonomia.trim();
    const idioma = form.idioma.trim();
    const tonoTutor = form.tonoTutor.trim();

    const estiloAprendizaje: {
      prefiere?: string;
      ritmo_sesion?: string;
      reaccion_ante_errores?: string;
      nivel_autonomia?: string;
    } = {};

    if (prefiere) estiloAprendizaje.prefiere = prefiere;
    if (ritmoSesion) estiloAprendizaje.ritmo_sesion = ritmoSesion;
    if (reaccionAnteErrores) estiloAprendizaje.reaccion_ante_errores = reaccionAnteErrores;
    if (nivelAutonomia) estiloAprendizaje.nivel_autonomia = nivelAutonomia;

    const preferenciasComunicacion: { idioma?: string; tono_tutor?: string } = {};
    if (idioma) preferenciasComunicacion.idioma = idioma;
    if (tonoTutor) preferenciasComunicacion.tono_tutor = tonoTutor;

    const profile: InitialStudentProfile = {};

    if (alias) profile.alias = alias;
    if (lenguajePrincipal) profile.lenguaje_principal = lenguajePrincipal;
    if (objetivoDeclarado) profile.objetivo_declarado = objetivoDeclarado;
    if (Object.keys(estiloAprendizaje).length > 0) profile.estilo_aprendizaje = estiloAprendizaje;
    if (Object.keys(preferenciasComunicacion).length > 0) {
      profile.preferencias_comunicacion = preferenciasComunicacion;
    }

    return Object.keys(profile).length > 0 ? profile : undefined;
  }

  private toSessionRequest(tutorId: number, form: SessionForm): StudySessionRequest {
    return {
      tutorId,
      name: form.name.trim(),
      goal: form.goal.trim(),
    };
  }

  private emptyTutorForm(): TutorForm {
    return {
      name: '',
      description: '',
      systemPromptContent: 'Actua como un tutor util, claro y socratico.',
      geminiModel: 'gemini-2.0-flash',
    };
  }

  private emptyStudentProfileForm(): StudentProfileForm {
    return {
      alias: '',
      lenguajePrincipal: '',
      objetivoDeclarado: '',
      prefiere: '',
      ritmoSesion: '',
      reaccionAnteErrores: '',
      nivelAutonomia: '',
      idioma: '',
      tonoTutor: '',
    };
  }

  private emptySessionForm(): SessionForm {
    return { name: '', goal: '' };
  }

  private sortSessions(sessions: StudySession[]): StudySession[] {
    return [...sessions].sort((first, second) => this.sessionTime(second) - this.sessionTime(first));
  }

  private sortTutorsBySessionActivity(tutors: Tutor[], sessions: StudySession[]): Tutor[] {
    return [...tutors].sort((first, second) => {
      const firstLatest = this.latestSessionTime(first.id, sessions) || Date.parse(first.updatedAtUtc);
      const secondLatest = this.latestSessionTime(second.id, sessions) || Date.parse(second.updatedAtUtc);

      return secondLatest - firstLatest;
    });
  }

  private latestSessionTime(tutorId: number, sessions: StudySession[]): number {
    return Math.max(
      0,
      ...sessions
        .filter((session) => session.tutorId === tutorId)
        .map((session) => this.sessionTime(session)),
    );
  }

  private sessionTime(session: StudySession): number {
    return Date.parse(session.updatedAtUtc || session.createdAtUtc) || 0;
  }

  private expandTutor(tutorId: number): void {
    this.expandedTutorIds.update((expanded) => new Set(expanded).add(tutorId));
  }
}
