import { DOCUMENT } from '@angular/common';
import { Injectable, inject, signal } from '@angular/core';

type Theme = 'dark' | 'light';

@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly document = inject(DOCUMENT);

  readonly theme = signal<Theme>('dark');

  constructor() {
    const savedTheme = this.getSavedTheme();

    if (savedTheme) {
      this.applyTheme(savedTheme);
    }
  }

  toggleTheme(): void {
    this.applyTheme(this.theme() === 'dark' ? 'light' : 'dark');
  }

  private applyTheme(theme: Theme): void {
    this.theme.set(theme);

    if (theme === 'light') {
      this.document.documentElement.setAttribute('data-theme', 'light');
    } else {
      this.document.documentElement.removeAttribute('data-theme');
    }

    if (typeof localStorage !== 'undefined') {
      localStorage.setItem('synapse-theme', theme);
    }
  }

  private getSavedTheme(): Theme | null {
    if (typeof localStorage === 'undefined') {
      return null;
    }

    const theme = localStorage.getItem('synapse-theme');
    return theme === 'light' || theme === 'dark' ? theme : null;
  }
}
