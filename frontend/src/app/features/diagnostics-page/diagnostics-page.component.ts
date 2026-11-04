import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';

import type { Tutor } from '../../core/models';
import { TutorService } from '../../core/services/tutor.service';
import { AppStateService } from '../../core/state/app-state.service';
import { DrawerComponent } from '../drawer/drawer.component';

@Component({
  selector: 'app-diagnostics-page',
  imports: [DrawerComponent],
  templateUrl: './diagnostics-page.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DiagnosticsPageComponent {
  private readonly router = inject(Router);
  private readonly tutorService = inject(TutorService);
  private readonly destroyRef = inject(DestroyRef);

  readonly appState = inject(AppStateService);
  readonly tutors = signal<Tutor[]>([]);

  constructor() {
    this.tutorService
      .getTutors()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (tutors) => {
          this.tutors.set(tutors);
          if (!this.appState.activeTutor() && tutors.length > 0) {
            this.appState.selectTutor(tutors[0]);
          }
        },
      });
  }

  selectTutorById(rawId: string): void {
    const id = Number.parseInt(rawId, 10);
    const tutor = this.tutors().find((item) => item.id === id) ?? null;
    if (tutor) {
      this.appState.selectTutor(tutor);
    }
  }

  backToChat(): void {
    const tutor = this.appState.activeTutor();
    const session = this.appState.activeSession();
    if (tutor && session) {
      void this.router.navigate(['/tutors', tutor.id, 'sessions', session.id]);
      return;
    }

    if (tutor) {
      void this.router.navigate(['/tutors', tutor.id]);
      return;
    }

    void this.router.navigate(['/']);
  }
}
