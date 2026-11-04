import { DOCUMENT } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  Input,
  OnChanges,
  OnDestroy,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { DomSanitizer, type SafeHtml } from '@angular/platform-browser';

import { ThemeService } from '../../../core/state/theme.service';

type MermaidApi = typeof import('mermaid')['default'];

let mermaidLoader: Promise<MermaidApi> | null = null;
let diagramCounter = 0;

function loadMermaid(): Promise<MermaidApi> {
  mermaidLoader ??= import('mermaid').then((module) => module.default);
  return mermaidLoader;
}

/**
 * Renders a Mermaid diagram lazily (the library is only downloaded the first time
 * a diagram appears). Uses `securityLevel: 'strict'` so labels cannot inject HTML/JS.
 */
@Component({
  selector: 'app-mermaid-diagram',
  templateUrl: './mermaid-diagram.component.html',
  styleUrl: './mermaid-diagram.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MermaidDiagramComponent implements OnChanges, OnDestroy {
  /** Mermaid source code. */
  @Input({ required: true }) code = '';
  /** When true (e.g. while the answer is still streaming) only the source is shown. */
  @Input() deferRender = false;

  private readonly sanitizer = inject(DomSanitizer);
  private readonly themeService = inject(ThemeService);
  private readonly document = inject(DOCUMENT);
  private renderToken = 0;
  private copyTimer: ReturnType<typeof setTimeout> | null = null;

  readonly svg = signal<SafeHtml | null>(null);
  readonly error = signal<string | null>(null);
  readonly rendering = signal(false);
  readonly showSource = signal(false);
  readonly copied = signal(false);

  constructor() {
    effect(() => {
      this.themeService.theme();
      untracked(() => void this.render());
    });
  }

  ngOnChanges(): void {
    void this.render();
  }

  ngOnDestroy(): void {
    this.renderToken++;
    if (this.copyTimer) {
      clearTimeout(this.copyTimer);
    }
  }

  toggleSource(): void {
    this.showSource.update((value) => !value);
  }

  async copySource(): Promise<void> {
    try {
      await globalThis.navigator?.clipboard?.writeText(this.code);
      this.copied.set(true);
      if (this.copyTimer) {
        clearTimeout(this.copyTimer);
      }
      this.copyTimer = setTimeout(() => this.copied.set(false), 2000);
    } catch {
      // Clipboard unavailable (non-secure context); ignore.
    }
  }

  private async render(): Promise<void> {
    const source = this.code.trim();
    const token = ++this.renderToken;

    if (this.deferRender || !source) {
      this.rendering.set(false);
      return;
    }

    this.rendering.set(true);
    const elementId = `synapse-mermaid-${++diagramCounter}`;

    try {
      const mermaid = await loadMermaid();
      mermaid.initialize({
        startOnLoad: false,
        securityLevel: 'strict',
        theme: this.themeService.theme() === 'dark' ? 'dark' : 'neutral',
        fontFamily: 'Inter, ui-sans-serif, system-ui, sans-serif',
      });

      const { svg } = await mermaid.render(elementId, source);
      if (token !== this.renderToken) {
        return;
      }

      this.svg.set(this.sanitizer.bypassSecurityTrustHtml(svg));
      this.error.set(null);
    } catch (err: unknown) {
      if (token !== this.renderToken) {
        return;
      }

      this.svg.set(null);
      this.error.set(err instanceof Error ? err.message.split('\n')[0] : 'Sintaxis de diagrama inválida.');
      // Mermaid leaves a temporary error node in <body> when parsing fails.
      this.document.getElementById(`d${elementId}`)?.remove();
      this.document.getElementById(elementId)?.remove();
    } finally {
      if (token === this.renderToken) {
        this.rendering.set(false);
      }
    }
  }
}
