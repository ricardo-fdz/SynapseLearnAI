import { Injectable, signal } from '@angular/core';

import type { ContextLoadProfile, StudySession, Tutor } from '../models';

@Injectable({ providedIn: 'root' })
export class AppStateService {
  private readonly activeTutorSignal = signal<Tutor | null>(null);
  private readonly activeSessionSignal = signal<StudySession | null>(null);
  private readonly activeProfileSignal = signal<ContextLoadProfile>('Standard');
  private readonly drawerOpenSignal = signal(false);

  readonly activeTutor = this.activeTutorSignal.asReadonly();
  readonly activeSession = this.activeSessionSignal.asReadonly();
  readonly activeProfile = this.activeProfileSignal.asReadonly();
  readonly drawerOpen = this.drawerOpenSignal.asReadonly();

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

  selectProfile(profile: ContextLoadProfile): void {
    this.activeProfileSignal.set(profile);
  }

  openDrawer(): void {
    this.drawerOpenSignal.set(true);
  }

  closeDrawer(): void {
    this.drawerOpenSignal.set(false);
  }

  toggleDrawer(): void {
    this.drawerOpenSignal.update((open) => !open);
  }

  clearActiveSelection(): void {
    this.activeTutorSignal.set(null);
    this.activeSessionSignal.set(null);
  }
}
