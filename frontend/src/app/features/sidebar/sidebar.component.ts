import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  EventEmitter,
  HostListener,
  Input,
  Output,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { catchError, finalize, forkJoin, of } from 'rxjs';

import { SessionService, type StudySessionRequest } from '../../core/services/session.service';
import {
  TutorService,
  type CreateTutorRequest,
  type InitialStudentProfile,
  type TutorRequest,
} from '../../core/services/tutor.service';
import { AppStateService } from '../../core/state/app-state.service';
import { ModalComponent } from '../../shared/components/modal/modal.component';
import type { LlmProfileOption, StudySession, Tutor } from '../../core/models';

interface TutorForm {
  name: string;
  description: string;
  systemPromptContent: string;
  geminiModel: string;
  llmProfile: string;
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

export interface SelectOption {
  value: string;
  label: string;
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
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  readonly appState = inject(AppStateService);

  readonly tutors = signal<Tutor[]>([]);
  readonly sessions = signal<StudySession[]>([]);
  readonly llmProfiles = signal<LlmProfileOption[]>([]);
  readonly defaultLlmProfile = signal('gemini-default');
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly error = signal<string | null>(null);
  readonly formError = signal<string | null>(null);
  readonly showTutorForm = signal(false);
  readonly createTutorStep = signal<1 | 2>(1);
  readonly editingTutorId = signal<number | null>(null);
  readonly sessionFormTutorId = signal<number | null>(null);
  readonly editingSessionId = signal<number | null>(null);
  readonly pendingDeleteTutor = signal<Tutor | null>(null);
  readonly pendingDeleteSession = signal<StudySession | null>(null);
  readonly expandedTutorIds = signal<ReadonlySet<number>>(new Set<number>());
  readonly openMenuId = signal<string | null>(null);
  readonly hasTutors = computed(() => this.tutors().length > 0);

  readonly learningPreferenceOptions: ReadonlyArray<SelectOption> = [
    { value: 'analogías', label: 'Analogías y metáforas' },
    { value: 'ejemplos_directos', label: 'Ejemplos directos' },
    { value: 'proyectos', label: 'Basado en proyectos' },
    { value: 'combinacion', label: 'Combinación equilibrada' },
  ];
  readonly sessionPaceOptions: ReadonlyArray<SelectOption> = [
    { value: 'cortas_intensas', label: 'Sesiones cortas e intensas' },
    { value: 'largas_progresivas', label: 'Sesiones largas y progresivas' },
  ];
  readonly errorReactionOptions: ReadonlyArray<SelectOption> = [
    { value: 'se_frustra_rapido', label: 'Se frustra rápido (requiere tacto)' },
    { value: 'resiliente', label: 'Resiliente ante retos' },
    { value: 'neutral', label: 'Neutral' },
  ];
  readonly autonomyOptions: ReadonlyArray<SelectOption> = [
    { value: 'necesita_mucha_guia', label: 'Necesita guía paso a paso' },
    { value: 'pistas_minimas', label: 'Prefiere pistas mínimas' },
    { value: 'muy_autonomo', label: 'Muy autónomo' },
  ];
  readonly languageOptions: ReadonlyArray<SelectOption> = [
    { value: 'espanol', label: 'Español' },
    { value: 'ingles', label: 'Inglés' },
    { value: 'combinacion', label: 'Bilingüe (Español / Inglés)' },
  ];
  readonly toneOptions: ReadonlyArray<SelectOption> = [
    { value: 'estricto_directo', label: 'Estricto y directo' },
    { value: 'alentador_paciente', label: 'Alentador y paciente' },
    { value: 'neutral', label: 'Neutral y objetivo' },
  ];

  newTutor: TutorForm = this.emptyTutorForm();
  newStudentProfile: StudentProfileForm = this.emptyStudentProfileForm();
  editTutor: TutorForm = this.emptyTutorForm();
  newSession: SessionForm = this.emptySessionForm();
  editSession: SessionForm = this.emptySessionForm();

  private lastHandledCreateTutorTrigger = 0;
  private lastHandledCreateSessionNonce = 0;

  constructor() {
    this.loadData();

    effect(() => {
      const trigger = this.appState.createTutorTrigger();
      if (trigger > this.lastHandledCreateTutorTrigger) {
        this.lastHandledCreateTutorTrigger = trigger;
        this.openCreateTutor();
      }
    });

    effect(() => {
      const req = this.appState.createSessionForTutorTrigger();
      if (req && req.nonce > this.lastHandledCreateSessionNonce) {
        this.lastHandledCreateSessionNonce = req.nonce;
        const tutor = this.tutors().find((t) => t.id === req.tutorId);
        if (tutor) {
          this.openCreateSession(tutor);
        }
      }
    });

    effect(() => {
      const activeSession = this.appState.activeSession();
      if (!activeSession) {
        return;
      }

      const currentSessions = this.sessions();
      const existing = currentSessions.find((s) => s.id === activeSession.id);
      if (existing && existing.updatedAtUtc !== activeSession.updatedAtUtc) {
        const updatedSessions = this.sortSessions(
          currentSessions.map((s) => (s.id === activeSession.id ? activeSession : s)),
        );
        this.sessions.set(updatedSessions);
        this.tutors.set(this.sortTutorsBySessionActivity(this.tutors(), updatedSessions));
      }
    });

    effect(() => {
      const requestedTutorId = this.appState.requestedTutorId();
      const requestedSessionId = this.appState.requestedSessionId();
      if (this.loading() || requestedTutorId === null) {
        return;
      }

      const tutors = this.tutors();
      const sessions = this.sessions();
      const tutor = tutors.find((item) => item.id === requestedTutorId) ?? null;
      if (!tutor) {
        return;
      }

      if (this.appState.activeTutor()?.id !== tutor.id) {
        this.appState.selectTutor(tutor);
        this.expandTutor(tutor.id);
      }

      if (requestedSessionId !== null) {
        const session =
          sessions.find((item) => item.id === requestedSessionId && item.tutorId === tutor.id) ??
          null;
        if (session && this.appState.activeSession()?.id !== session.id) {
          this.appState.selectSession(session);
        }
      } else if (this.appState.activeSession() !== null) {
        this.appState.selectSession(null);
      }
    });
  }

  @HostListener('document:click')
  onDocumentClick(): void {
    if (this.openMenuId() !== null) {
      this.closeMenu();
    }
  }

  @HostListener('document:keydown.escape')
  onDocumentEscape(): void {
    if (this.openMenuId() !== null) {
      this.closeMenu();
    }
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

  toggleMenu(id: string, event?: MouseEvent): void {
    event?.stopPropagation();
    this.openMenuId.update((current) => (current === id ? null : id));
  }

  closeMenu(): void {
    this.openMenuId.set(null);
  }

  selectTutor(tutor: Tutor): void {
    this.closeMenu();
    this.appState.selectTutor(tutor);
    this.expandTutor(tutor.id);
    this.navigateToSelection(tutor.id, null);
  }

  selectSession(tutor: Tutor, session: StudySession): void {
    this.closeMenu();

    if (this.appState.activeTutor()?.id !== tutor.id) {
      this.appState.selectTutor(tutor);
    }

    this.expandTutor(tutor.id);
    this.appState.selectSession(session);
    this.navigateToSelection(tutor.id, session.id);
    this.sessionSelected.emit();
  }

  openCreateTutor(): void {
    this.newTutor = this.emptyTutorForm();
    this.newStudentProfile = this.emptyStudentProfileForm();
    this.createTutorStep.set(1);
    this.showTutorForm.set(true);
    this.editingTutorId.set(null);
    this.formError.set(null);
    this.closeMenu();
  }

  cancelTutorForm(): void {
    this.showTutorForm.set(false);
    this.createTutorStep.set(1);
    this.editingTutorId.set(null);
    this.formError.set(null);
  }

  goToTutorProfileStep(): void {
    const request = this.toCreateTutorRequest(this.newTutor);

    if (!request.name) {
      this.formError.set('El tutor necesita un nombre.');
      return;
    }

    this.formError.set(null);
    this.createTutorStep.set(2);
  }

  goToTutorDataStep(): void {
    this.formError.set(null);
    this.createTutorStep.set(1);
  }

  createTutor(): void {
    const request = this.toCreateTutorRequest(this.newTutor, this.newStudentProfile);

    if (!request.name) {
      this.formError.set('El tutor necesita un nombre.');
      return;
    }

    this.saving.set(true);
    this.formError.set(null);

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
          this.navigateToSelection(tutor.id, null);
          this.loadData(tutor.id);
        },
        error: () => this.formError.set('No se pudo crear el tutor.'),
      });
  }

  startEditTutor(tutor: Tutor): void {
    this.editTutor = {
      name: tutor.name,
      description: tutor.description,
      systemPromptContent: tutor.systemPromptContent,
      geminiModel: tutor.geminiModel,
      llmProfile: tutor.llmProfile || this.defaultLlmProfile(),
    };
    this.editingTutorId.set(tutor.id);
    this.showTutorForm.set(false);
    this.formError.set(null);
    this.closeMenu();
  }

  cancelEditTutor(): void {
    this.editingTutorId.set(null);
    this.formError.set(null);
  }

  updateTutor(tutor: Tutor): void {
    const request = this.toTutorRequest(this.editTutor);

    if (!request.name) {
      this.formError.set('El tutor necesita un nombre.');
      return;
    }

    this.saving.set(true);
    this.formError.set(null);

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
        error: () => this.formError.set('No se pudo actualizar el tutor.'),
      });
  }

  requestDeleteTutor(tutor: Tutor): void {
    this.closeMenu();
    this.pendingDeleteTutor.set(tutor);
  }

  cancelDeleteTutor(): void {
    this.pendingDeleteTutor.set(null);
  }

  confirmDeleteTutor(): void {
    const tutor = this.pendingDeleteTutor();
    if (!tutor || this.saving()) {
      return;
    }

    this.pendingDeleteTutor.set(null);
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
            this.appState.setRouteSelection(null, null);
            this.navigateToSelection(null, null, true);
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
    this.formError.set(null);
    this.expandTutor(tutor.id);
    this.closeMenu();
  }

  cancelSessionForm(): void {
    this.sessionFormTutorId.set(null);
    this.editingSessionId.set(null);
    this.formError.set(null);
  }

  createSession(tutor: Tutor): void {
    const request = this.toSessionRequest(tutor.id, this.newSession);

    if (!request.name) {
      this.formError.set('La sesión necesita un nombre.');
      return;
    }

    this.saving.set(true);
    this.formError.set(null);

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
          this.navigateToSelection(tutor.id, session.id);
          this.loadData(tutor.id, session.id);
          this.sessionSelected.emit();
        },
        error: () => this.formError.set('No se pudo crear la sesión.'),
      });
  }

  startEditSession(session: StudySession): void {
    this.editSession = { name: session.name, goal: session.goal };
    this.editingSessionId.set(session.id);
    this.sessionFormTutorId.set(null);
    this.formError.set(null);
    this.closeMenu();
  }

  updateSession(session: StudySession): void {
    const request = this.toSessionRequest(session.tutorId, this.editSession);

    if (!request.name) {
      this.formError.set('La sesión necesita un nombre.');
      return;
    }

    this.saving.set(true);
    this.formError.set(null);

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
        error: () => this.formError.set('No se pudo actualizar la sesión.'),
      });
  }

  requestDeleteSession(session: StudySession): void {
    this.closeMenu();
    this.pendingDeleteSession.set(session);
  }

  cancelDeleteSession(): void {
    this.pendingDeleteSession.set(null);
  }

  confirmDeleteSession(): void {
    const session = this.pendingDeleteSession();
    if (!session || this.saving()) {
      return;
    }

    this.pendingDeleteSession.set(null);
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
          const activeTutorId = this.appState.activeTutor()?.id ?? null;
          if (this.appState.activeSession()?.id === session.id) {
            this.appState.selectSession(null);
            this.appState.setRouteSelection(activeTutorId, null);
            this.navigateToSelection(activeTutorId, null, true);
          }

          this.loadData(activeTutorId);
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
      profilesCatalog: this.tutorService
        .getLlmProfiles()
        .pipe(catchError(() => of({ defaultProfile: 'gemini-default', profiles: [] }))),
    })
      .pipe(
        finalize(() => this.loading.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: ({ tutors, sessions, profilesCatalog }) => {
          if (profilesCatalog.defaultProfile) {
            this.defaultLlmProfile.set(profilesCatalog.defaultProfile);
          }
          this.llmProfiles.set(profilesCatalog.profiles ?? []);

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
    const routeTutorId = this.appState.requestedTutorId();
    const routeSessionId = this.appState.requestedSessionId();
    const activeTutorId = selectTutorId ?? routeTutorId ?? this.appState.activeTutor()?.id ?? null;
    const activeSessionId =
      selectSessionId ?? routeSessionId ?? this.appState.activeSession()?.id ?? null;
    const activeTutor = tutors.find((tutor) => tutor.id === activeTutorId) ?? null;
    const activeSession =
      sessions.find(
        (session) =>
          session.id === activeSessionId && (!activeTutor || session.tutorId === activeTutor.id),
      ) ?? null;

    if (!activeTutor && !activeSession && sessions.length > 0) {
      const latestSession = this.sortSessions(sessions)[0];
      const latestTutor = tutors.find((tutor) => tutor.id === latestSession.tutorId) ?? null;

      if (latestTutor) {
        this.appState.selectTutor(latestTutor);
        this.appState.selectSession(latestSession);
        this.expandTutor(latestTutor.id);
        this.navigateToSelection(latestTutor.id, latestSession.id, true);
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

  private navigateToSelection(
    tutorId: number | null,
    sessionId: number | null,
    replaceUrl = false,
  ): void {
    if (this.router.url.startsWith('/diagnostico')) {
      return;
    }

    if (tutorId && sessionId) {
      void this.router.navigate(['/tutors', tutorId, 'sessions', sessionId], { replaceUrl });
      return;
    }

    if (tutorId) {
      void this.router.navigate(['/tutors', tutorId], { replaceUrl });
      return;
    }

    void this.router.navigate(['/'], { replaceUrl });
  }

  private toTutorRequest(form: TutorForm): TutorRequest {
    const geminiModel = form.geminiModel.trim();
    const llmProfile = form.llmProfile.trim();
    return {
      name: form.name.trim(),
      description: form.description.trim(),
      systemPromptContent: form.systemPromptContent.trim(),
      ...(geminiModel ? { geminiModel } : {}),
      ...(llmProfile ? { llmProfile } : {}),
    };
  }

  private toCreateTutorRequest(
    tutorForm: TutorForm,
    profileForm?: StudentProfileForm,
  ): CreateTutorRequest {
    const profile = profileForm ? this.toInitialStudentProfile(profileForm) : undefined;
    const geminiModel = tutorForm.geminiModel.trim();
    const llmProfile = tutorForm.llmProfile.trim();

    return {
      name: tutorForm.name.trim(),
      description: tutorForm.description.trim(),
      systemPromptContent: tutorForm.systemPromptContent.trim(),
      ...(geminiModel ? { geminiModel } : {}),
      ...(llmProfile ? { llmProfile } : {}),
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
      systemPromptContent: 'Actúa como un tutor útil, claro y socrático.',
      geminiModel: '',
      llmProfile: this.defaultLlmProfile(),
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
