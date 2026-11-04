import { ChangeDetectionStrategy, Component, DestroyRef, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';

import { AppStateService } from '../../core/state/app-state.service';
import { ChatComponent } from '../chat/chat.component';
import { DrawerComponent } from '../drawer/drawer.component';
import { SidebarComponent } from '../sidebar/sidebar.component';

@Component({
  selector: 'app-main-layout',
  imports: [SidebarComponent, ChatComponent, DrawerComponent],
  templateUrl: './main-layout.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MainLayoutComponent {
  readonly appState = inject(AppStateService);

  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);

  constructor() {
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      const tutorId = this.parsePositiveInt(params.get('tutorId'));
      const sessionId = this.parsePositiveInt(params.get('sessionId'));
      this.appState.setRouteSelection(tutorId, sessionId);
    });
  }

  private parsePositiveInt(raw: string | null): number | null {
    if (!raw) {
      return null;
    }

    const parsed = Number.parseInt(raw, 10);
    return Number.isInteger(parsed) && parsed > 0 ? parsed : null;
  }
}
