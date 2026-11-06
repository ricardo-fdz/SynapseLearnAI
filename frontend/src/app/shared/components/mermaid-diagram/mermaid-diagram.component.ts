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

  private sanitizeMermaidSource(code: string): string {
    // 1. Decode HTML entities that LLMs often emit in markdown blocks
    let fixed = code
      .replace(/&lt;/g, '<')
      .replace(/&gt;/g, '>')
      .replace(/&amp;/g, '&')
      .replace(/&quot;/g, "'");

    // 2. Fix unquoted subgraph titles: subgraph ID [Title with special chars] -> subgraph ID ["Title with special chars"]
    fixed = fixed.replace(/subgraph\s+([A-Za-z0-9_]+)\s*\[([^"\]\n]+)\]/g, (_match, id, title) => {
      const cleanTitle = title.trim().replace(/"/g, "'");
      return `subgraph ${id} ["${cleanTitle}"]`;
    });

    // 3. Fix unquoted node labels: id[Some text with "quotes" or special chars] -> id["Some text with 'quotes' or special chars"]
    fixed = fixed.replace(/([A-Za-z0-9_]+)\[([^"\n\]][^\n\]]*)\]/g, (match, id, label) => {
      if (label.startsWith('"') && label.endsWith('"')) {
        return match;
      }
      const cleanLabel = label.trim().replace(/"/g, "'");
      return `${id}["${cleanLabel}"]`;
    });

    // 4. Fix decision nodes: id{Some text with special chars} -> id{"Some text"}
    fixed = fixed.replace(/([A-Za-z0-9_]+)\{([^"\n\}][^\n\}]*)\}/g, (match, id, label) => {
      if (label.startsWith('"') && label.endsWith('"')) {
        return match;
      }
      const cleanLabel = label.trim().replace(/"/g, "'");
      return `${id}{"${cleanLabel}"}`;
    });

    // 5. Fix rounded/parenthesis nodes: id(Some text) -> id("Some text") if contains special characters
    fixed = fixed.replace(/([A-Za-z0-9_]+)\(([^"\n\)][^\n\)]*)\)/g, (match, id, label) => {
      if (label.startsWith('"') && label.endsWith('"')) {
        return match;
      }
      if (/[<>&"';]/.test(label)) {
        const cleanLabel = label.trim().replace(/"/g, "'");
        return `${id}("${cleanLabel}")`;
      }
      return match;
    });

    return fixed;
  }

  private async render(): Promise<void> {
    const rawSource = this.code.trim();
    const token = ++this.renderToken;

    if (this.deferRender || !rawSource) {
      this.rendering.set(false);
      return;
    }

    this.rendering.set(true);
    const elementId = `synapse-mermaid-${++diagramCounter}`;
    const sanitizedSource = this.sanitizeMermaidSource(rawSource);

    try {
      const mermaid = await loadMermaid();
      mermaid.initialize({
        startOnLoad: false,
        securityLevel: 'strict',
        suppressErrorRendering: true,
        theme: this.themeService.theme() === 'dark' ? 'dark' : 'neutral',
        fontFamily: 'Inter, ui-sans-serif, system-ui, sans-serif',
      });

      let svgResult: { svg: string };
      try {
        svgResult = await mermaid.render(elementId, sanitizedSource);
      } catch (renderError) {
        // Fallback to original source if sanitation altered something unexpectedly
        if (sanitizedSource !== rawSource) {
          svgResult = await mermaid.render(elementId + '-raw', rawSource);
        } else {
          throw renderError;
        }
      }

      if (token !== this.renderToken) {
        return;
      }

      this.svg.set(this.sanitizer.bypassSecurityTrustHtml(svgResult.svg));
      this.error.set(null);
    } catch (err: unknown) {
      if (token !== this.renderToken) {
        return;
      }

      this.svg.set(null);
      this.error.set(err instanceof Error ? err.message.split('\n')[0] : 'Sintaxis de diagrama inválida.');
      // Clean up any error element or bomb banner Mermaid might have appended to <body>
      this.document.querySelectorAll(`[id^="d${elementId}"], [id^="${elementId}"], #mermaid-error-svg, svg[aria-roledescription="error"]`).forEach((el) => el.remove());
    } finally {
      if (token === this.renderToken) {
        this.rendering.set(false);
      }
    }
  }
}
