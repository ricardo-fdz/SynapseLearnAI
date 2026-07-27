import { Routes } from '@angular/router';

import { DiagnosticsPageComponent } from './features/diagnostics-page/diagnostics-page.component';
import { MainLayoutComponent } from './features/main-layout/main-layout.component';

export const routes: Routes = [
  { path: '', component: MainLayoutComponent },
  { path: 'diagnostico', component: DiagnosticsPageComponent },
  { path: '**', redirectTo: '' },
];
