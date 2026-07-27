import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

import { AppStateService } from '../../core/state/app-state.service';
import { ThemeService } from '../../core/state/theme.service';
import { ChatComponent } from '../chat/chat.component';
import { DrawerComponent } from '../drawer/drawer.component';
import { SidebarComponent } from '../sidebar/sidebar.component';

@Component({
  selector: 'app-main-layout',
  imports: [RouterLink, SidebarComponent, ChatComponent, DrawerComponent],
  templateUrl: './main-layout.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MainLayoutComponent {
  readonly appState = inject(AppStateService);
  readonly themeService = inject(ThemeService);
  readonly mobileSidebarOpen = signal(false);

  openMobileSidebar(): void {
    this.mobileSidebarOpen.set(true);
  }

  closeMobileSidebar(): void {
    this.mobileSidebarOpen.set(false);
  }
}
