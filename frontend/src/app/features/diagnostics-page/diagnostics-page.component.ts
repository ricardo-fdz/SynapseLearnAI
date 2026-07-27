import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { Router } from '@angular/router';

import { DrawerComponent } from '../drawer/drawer.component';

@Component({
  selector: 'app-diagnostics-page',
  imports: [DrawerComponent],
  templateUrl: './diagnostics-page.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DiagnosticsPageComponent {
  private readonly router = inject(Router);

  backToChat(): void {
    void this.router.navigateByUrl('/');
  }
}
