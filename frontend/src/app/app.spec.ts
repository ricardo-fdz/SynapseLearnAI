import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { App } from './app';
import { routes } from './app.routes';
import { AppStateService } from './core/state/app-state.service';

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter(routes), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
  });

  it('should create the app', () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;
    expect(app).toBeTruthy();
  });

  it('should render the main layout and sync deep-linked route params', async () => {
    const fixture = TestBed.createComponent(App);
    const router = TestBed.inject(Router);
    const appState = TestBed.inject(AppStateService);

    await router.navigateByUrl('/tutors/3/sessions/14');
    fixture.detectChanges();
    await fixture.whenStable();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.textContent).toContain('Synapse Learn');
    expect(compiled.querySelector('app-sidebar')).toBeTruthy();
    expect(compiled.querySelector('app-chat')).toBeTruthy();
    expect(appState.requestedTutorId()).toBe(3);
    expect(appState.requestedSessionId()).toBe(14);
  });
});
