import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import katex from 'katex';
import { vi } from 'vitest';
import { MarkdownMessageComponent } from './markdown-message.component';

describe('MarkdownMessageComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [MarkdownMessageComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('escapes HTML when KaTeX throws an error', async () => {
    vi.spyOn(katex, 'renderToString').mockImplementation(() => {
      throw new Error('KaTeX failure');
    });

    const fixture = TestBed.createComponent(MarkdownMessageComponent);
    fixture.componentInstance.content = '$<img src=x onerror=alert(1)>$';
    fixture.componentInstance.ngOnChanges();
    fixture.detectChanges();
    await fixture.whenStable();

    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('img')).toBeNull();
    expect(el.querySelector('code')?.textContent).toBe('<img src=x onerror=alert(1)>');
  });

  it('does not treat currency values like $10 y $20 or $10-$20 as inline math, while rendering real $x^2$', async () => {
    const katexSpy = vi.spyOn(katex, 'renderToString');

    const fixture = TestBed.createComponent(MarkdownMessageComponent);
    fixture.componentInstance.content = 'El plan cuesta $10 y $20 (rango $10-$20), mientras que $x^2 + 1$ es álgebra.';
    fixture.componentInstance.ngOnChanges();
    fixture.detectChanges();
    await fixture.whenStable();

    expect(katexSpy).toHaveBeenCalledTimes(1);
    expect(katexSpy).toHaveBeenCalledWith('x^2 + 1', expect.objectContaining({ displayMode: false }));

    const el = fixture.nativeElement as HTMLElement;
    expect(el.textContent).toContain('$10 y $20');
    expect(el.textContent).toContain('$10-$20');
  });

  it('renders fenced code blocks with language header and GFM tables', async () => {
    const fixture = TestBed.createComponent(MarkdownMessageComponent);
    fixture.componentInstance.content = [
      '```typescript',
      'const answer = 42;',
      '```',
      '',
      '| Concepto | Nivel |',
      '| --- | --- |',
      '| Closures | 4 |',
    ].join('\n');
    fixture.componentInstance.ngOnChanges();
    fixture.detectChanges();
    await fixture.whenStable();

    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.code-block-lang')?.textContent?.trim()).toBe('typescript');
    expect(el.querySelector('.code-copy-btn')?.textContent?.trim()).toBe('Copiar');
    expect(el.querySelectorAll('table thead th').length).toBe(2);
    expect(el.querySelector('table tbody td')?.textContent?.trim()).toBe('Closures');
  });

  it('renders safe https links and blocks unsafe javascript: links', async () => {
    const fixture = TestBed.createComponent(MarkdownMessageComponent);
    fixture.componentInstance.content =
      'Consulta la [documentación de Angular](https://angular.dev) y evita [payload](javascript:alert(1)).';
    fixture.componentInstance.ngOnChanges();
    fixture.detectChanges();
    await fixture.whenStable();

    const el = fixture.nativeElement as HTMLElement;
    const anchors = el.querySelectorAll('a');
    expect(anchors.length).toBe(1);
    expect(anchors[0].getAttribute('href')).toBe('https://angular.dev');
    expect(anchors[0].getAttribute('target')).toBe('_blank');
    expect(anchors[0].getAttribute('rel')).toBe('noopener noreferrer');
    expect(anchors[0].textContent).toBe('documentación de Angular');
    expect(el.textContent).toContain('[payload](javascript:alert(1))');
  });

  it('renders interactive listening block without revealing transcript initially', async () => {
    const fixture = TestBed.createComponent(MarkdownMessageComponent);
    fixture.componentInstance.content = [
      'Por favor escucha el siguiente clip:',
      '```listening lang=en-US rate=0.85',
      'The quick brown fox jumps over the lazy dog.',
      '```',
    ].join('\n');
    fixture.componentInstance.ngOnChanges();
    fixture.detectChanges();
    await fixture.whenStable();

    const el = fixture.nativeElement as HTMLElement;
    const listeningCard = el.querySelector('.listening-card');
    expect(listeningCard).not.toBeNull();
    expect(el.querySelector('.listening-rate-toggle')?.textContent?.trim()).toBe('0.85x');
    expect(el.querySelector('.listening-play-button')?.textContent?.trim()).toContain('Reproducir');

    // Transcript is hidden by default
    expect(el.querySelector('.transcript-revealed')).toBeNull();

    // Toggle reveal
    const toggleBtn = el.querySelector('.transcript-toggle-button') as HTMLButtonElement;
    toggleBtn.click();
    fixture.detectChanges();
    await fixture.whenStable();

    expect(el.querySelector('.transcript-revealed')?.textContent?.trim()).toBe(
      'The quick brown fox jumps over the lazy dog.',
    );
  });

  it('renders mermaid diagram component for mermaid code blocks', async () => {
    const fixture = TestBed.createComponent(MarkdownMessageComponent);
    fixture.componentInstance.content = [
      '```mermaid',
      'graph TD',
      'A[Inicio] --> B[Fin]',
      '```',
    ].join('\n');
    fixture.componentInstance.ngOnChanges();
    fixture.detectChanges();
    await fixture.whenStable();

    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('app-mermaid-diagram')).not.toBeNull();
  });
});
