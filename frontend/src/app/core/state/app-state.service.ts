import { Injectable, signal } from '@angular/core';

import type { ContextLoadProfile, StudySession, Tutor } from '../models';

export type DiagnosticsDrawerTab = 'memory' | 'audit' | 'prompt';

@Injectable({ providedIn: 'root' })
export class AppStateService {
  private readonly activeTutorSignal = signal<Tutor | null>(null);
  private readonly activeSessionSignal = signal<StudySession | null>(null);
  private readonly activeProfileSignal = signal<ContextLoadProfile>('Standard');
  private readonly drawerOpenSignal = signal(false);
  private readonly drawerTabSignal = signal<DiagnosticsDrawerTab>('memory');
  private readonly mobileSidebarOpenSignal = signal(false);
  private readonly memoryVersionSignal = signal(0);
  private readonly requestedTutorIdSignal = signal<number | null>(null);
  private readonly requestedSessionIdSignal = signal<number | null>(null);
  private readonly createTutorTriggerSignal = signal(0);
  private readonly createSessionForTutorTriggerSignal = signal<{ tutorId: number; nonce: number } | null>(null);

  readonly activeTutor = this.activeTutorSignal.asReadonly();
  readonly activeSession = this.activeSessionSignal.asReadonly();
  readonly activeProfile = this.activeProfileSignal.asReadonly();
  readonly drawerOpen = this.drawerOpenSignal.asReadonly();
  readonly drawerTab = this.drawerTabSignal.asReadonly();
  readonly mobileSidebarOpen = this.mobileSidebarOpenSignal.asReadonly();
  readonly memoryVersion = this.memoryVersionSignal.asReadonly();
  readonly requestedTutorId = this.requestedTutorIdSignal.asReadonly();
  readonly requestedSessionId = this.requestedSessionIdSignal.asReadonly();
  readonly createTutorTrigger = this.createTutorTriggerSignal.asReadonly();
  readonly createSessionForTutorTrigger = this.createSessionForTutorTriggerSignal.asReadonly();

  setRouteSelection(tutorId: number | null, sessionId: number | null): void {
    this.requestedTutorIdSignal.set(tutorId);
    this.requestedSessionIdSignal.set(sessionId);
  }

  selectTutor(tutor: Tutor | null): void {
    this.activeTutorSignal.set(tutor);
    this.activeSessionSignal.set(null);
  }

  updateActiveTutor(tutor: Tutor): void {
    if (this.activeTutorSignal()?.id === tutor.id) {
      this.activeTutorSignal.set(tutor);
    }
  }

  selectSession(session: StudySession | null): void {
    const activeTutor = this.activeTutorSignal();

    if (session && activeTutor && session.tutorId !== activeTutor.id) {
      this.activeSessionSignal.set(null);
      return;
    }

    this.activeSessionSignal.set(session);
  }

  updateActiveSession(session: StudySession): void {
    if (this.activeSessionSignal()?.id === session.id) {
      this.activeSessionSignal.set(session);
    }
  }

  touchActiveSessionActivity(updatedAtUtc: string): void {
    const current = this.activeSessionSignal();
    if (current) {
      this.activeSessionSignal.set({ ...current, updatedAtUtc });
    }
  }

  selectProfile(profile: ContextLoadProfile): void {
    this.activeProfileSignal.set(profile);
  }

  bumpMemoryVersion(): void {
    this.memoryVersionSignal.update((v) => v + 1);
  }

  openDrawer(tab?: DiagnosticsDrawerTab): void {
    if (tab) {
      this.drawerTabSignal.set(tab);
    }
    this.drawerOpenSignal.set(true);
  }

  setDrawerTab(tab: DiagnosticsDrawerTab): void {
    this.drawerTabSignal.set(tab);
  }

  closeDrawer(): void {
    this.drawerOpenSignal.set(false);
  }

  toggleDrawer(): void {
    this.drawerOpenSignal.update((open) => !open);
  }

  openMobileSidebar(): void {
    this.mobileSidebarOpenSignal.set(true);
  }

  closeMobileSidebar(): void {
    this.mobileSidebarOpenSignal.set(false);
  }

  requestCreateTutor(): void {
    this.createTutorTriggerSignal.update((n) => n + 1);
  }

  requestCreateSession(tutorId: number): void {
    this.createSessionForTutorTriggerSignal.update((prev) => ({
      tutorId,
      nonce: (prev?.nonce ?? 0) + 1,
    }));
  }

  clearActiveSelection(): void {
    this.activeTutorSignal.set(null);
    this.activeSessionSignal.set(null);
  }
}
