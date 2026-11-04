import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, Input, OnChanges, OnDestroy, inject, signal } from '@angular/core';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import katex from 'katex';

import { SpeechService } from '../../../core/services/speech.service';
import { MermaidDiagramComponent } from '../mermaid-diagram/mermaid-diagram.component';

type InlineToken =
  | { type: 'text'; text: string }
  | { type: 'bold'; text: string }
  | { type: 'italic'; text: string }
  | { type: 'code'; text: string }
  | { type: 'link'; text: string; url: string }
  | { type: 'math'; html: SafeHtml };

export type MarkdownBlock =
  | { type: 'paragraph'; tokens: InlineToken[] }
  | { type: 'heading'; level: 1 | 2 | 3; tokens: InlineToken[] }
  | { type: 'quote'; tokens: InlineToken[] }
  | { type: 'unordered-list'; items: InlineToken[][] }
  | { type: 'ordered-list'; items: InlineToken[][] }
  | { type: 'code-block'; language: string; text: string }
  | { type: 'mermaid'; code: string }
  | {
      type: 'listening';
      blockId: string;
      text: string;
      lang: string;
      baseRate: number;
    }
  | { type: 'table'; headers: InlineToken[][]; rows: InlineToken[][][] }
  | { type: 'rule' }
  | { type: 'math'; html: SafeHtml };

@Component({
  selector: 'app-markdown-message',
  imports: [NgTemplateOutlet, MermaidDiagramComponent],
  templateUrl: './markdown-message.component.html',
  styleUrl: './markdown-message.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MarkdownMessageComponent implements OnChanges, OnDestroy {
  @Input({ required: true }) content = '';
  @Input() deferDiagrams = false;

  readonly blocks = signal<MarkdownBlock[]>([]);
  readonly copiedBlockIndex = signal<number | null>(null);
  readonly revealedListening = signal<Record<string, boolean>>({});
  readonly customRates = signal<Record<string, number>>({});

  readonly speechService = inject(SpeechService);
  private readonly sanitizer = inject(DomSanitizer);
  private copyResetTimer: ReturnType<typeof setTimeout> | null = null;

  ngOnChanges(): void {
    this.blocks.set(this.parseBlocks(this.content));
  }

  ngOnDestroy(): void {
    if (this.copyResetTimer) {
      clearTimeout(this.copyResetTimer);
    }
  }

  async copyCode(blockIndex: number, text: string): Promise<void> {
    try {
      await globalThis.navigator?.clipboard?.writeText(text);
      this.copiedBlockIndex.set(blockIndex);
      if (this.copyResetTimer) {
        clearTimeout(this.copyResetTimer);
      }
      this.copyResetTimer = setTimeout(() => {
        if (this.copiedBlockIndex() === blockIndex) {
          this.copiedBlockIndex.set(null);
        }
      }, 2000);
    } catch {
      // Clipboard API unavailable in non-secure context; ignore gracefully
    }
  }

  getRate(blockId: string, defaultRate: number): number {
    return this.customRates()[blockId] ?? defaultRate;
  }

  cycleRate(blockId: string, currentDefault: number): void {
    const current = this.getRate(blockId, currentDefault);
    let next = 1.0;
    if (Math.abs(current - 0.85) < 0.05 || Math.abs(current - 0.8) < 0.05) {
      next = 1.0;
    } else if (Math.abs(current - 1.0) < 0.05) {
      next = 0.75;
    } else if (Math.abs(current - 0.75) < 0.05) {
      next = 1.0;
    } else {
      next = 0.85;
    }
    this.customRates.update((rates) => ({ ...rates, [blockId]: next }));
  }

  isRevealed(blockId: string): boolean {
    return !!this.revealedListening()[blockId];
  }

  toggleTranscript(blockId: string): void {
    this.revealedListening.update((prev) => ({
      ...prev,
      [blockId]: !prev[blockId],
    }));
  }

  toggleListening(block: { blockId: string; text: string; lang: string; baseRate: number }): void {
    const rate = this.getRate(block.blockId, block.baseRate);
    this.speechService.toggle(block.blockId, block.text, {
      lang: block.lang,
      rate,
    });
  }

  private renderMath(latex: string, display: boolean): SafeHtml {
    try {
      const html = katex.renderToString(latex, {
        throwOnError: false,
        displayMode: display,
      });
      return this.sanitizer.bypassSecurityTrustHtml(html);
    } catch {
      return this.sanitizer.bypassSecurityTrustHtml(`<code>${this.escapeHtml(latex)}</code>`);
    }
  }

  private escapeHtml(value: string): string {
    return value
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&#39;');
  }

  private parseBlocks(markdown: string): MarkdownBlock[] {
    const displayMaths: string[] = [];
    const processed = markdown
      .replace(/\r\n/g, '\n')
      .replace(/\$\$([\s\S]*?)\$\$/g, (_match: string, latex: string) => {
        const index = displayMaths.length;
        displayMaths.push(latex.trim());
        return `\x00MATH${index}\x00`;
      });

    const lines = processed.split('\n');
    const blocks: MarkdownBlock[] = [];
    let index = 0;

    while (index < lines.length) {
      const line = lines[index];
      const trimmed = line.trim();

      const mathMatch = /^\x00MATH(\d+)\x00$/.exec(trimmed);
      if (mathMatch) {
        const latex = displayMaths[parseInt(mathMatch[1], 10)];
        if (latex) {
          blocks.push({ type: 'math', html: this.renderMath(latex, true) });
        }
        index += 1;
        continue;
      }

      if (!trimmed) {
        index += 1;
        continue;
      }

      if (trimmed.startsWith('```')) {
        const rawTag = trimmed.slice(3).trim();
        const codeLines: string[] = [];
        index += 1;

        while (index < lines.length && !lines[index].trim().startsWith('```')) {
          codeLines.push(lines[index]);
          index += 1;
        }

        const tagLower = rawTag.toLowerCase();
        const fullText = codeLines.join('\n');

        if (tagLower === 'mermaid') {
          blocks.push({
            type: 'mermaid',
            code: fullText,
          });
        } else if (tagLower === 'listening' || tagLower.startsWith('listening ') || tagLower.startsWith('listening:')) {
          const langMatch = /lang=([a-zA-Z0-9-]+)/i.exec(rawTag);
          const rateMatch = /rate=([0-9.]+)/i.exec(rawTag);
          const lang = langMatch ? langMatch[1] : 'en-US';
          const baseRate = rateMatch ? parseFloat(rateMatch[1]) : 0.85;
          const blockId = `listening-${blocks.length}-${Math.random().toString(36).slice(2, 7)}`;
          blocks.push({
            type: 'listening',
            blockId,
            text: fullText.trim(),
            lang,
            baseRate,
          });
        } else {
          blocks.push({ type: 'code-block', language: rawTag, text: fullText });
        }
        index += 1;
        continue;
      }

      if (
        this.isTableRow(trimmed) &&
        index + 1 < lines.length &&
        this.isTableSeparator(lines[index + 1].trim())
      ) {
        const headerCells = this.splitTableCells(trimmed);
        const headers = headerCells.map((cell) => this.parseInline(cell, displayMaths));
        index += 2;

        const rows: InlineToken[][][] = [];
        while (index < lines.length && this.isTableRow(lines[index].trim())) {
          const cells = this.splitTableCells(lines[index].trim());
          const normalized = headerCells.map((_h, colIdx) =>
            this.parseInline(cells[colIdx] ?? '', displayMaths),
          );
          rows.push(normalized);
          index += 1;
        }

        blocks.push({ type: 'table', headers, rows });
        continue;
      }

      if (/^---+$/.test(trimmed)) {
        blocks.push({ type: 'rule' });
        index += 1;
        continue;
      }

      const heading = /^(#{1,3})\s+(.+)$/.exec(trimmed);

      if (heading) {
        blocks.push({
          type: 'heading',
          level: heading[1].length as 1 | 2 | 3,
          tokens: this.parseInline(heading[2], displayMaths),
        });
        index += 1;
        continue;
      }

      if (trimmed.startsWith('> ')) {
        blocks.push({ type: 'quote', tokens: this.parseInline(trimmed.slice(2), displayMaths) });
        index += 1;
        continue;
      }

      if (/^[-*]\s+/.test(trimmed)) {
        const items: InlineToken[][] = [];

        while (index < lines.length && /^[-*]\s+/.test(lines[index].trim())) {
          items.push(this.parseInline(lines[index].trim().replace(/^[-*]\s+/, ''), displayMaths));
          index += 1;
        }

        blocks.push({ type: 'unordered-list', items });
        continue;
      }

      if (/^\d+[.)]\s+/.test(trimmed)) {
        const items: InlineToken[][] = [];

        while (index < lines.length && /^\d+[.)]\s+/.test(lines[index].trim())) {
          items.push(this.parseInline(lines[index].trim().replace(/^\d+[.)]\s+/, ''), displayMaths));
          index += 1;
        }

        blocks.push({ type: 'ordered-list', items });
        continue;
      }

      const paragraphLines = [trimmed];
      index += 1;

      while (index < lines.length && this.isParagraphContinuation(lines, index)) {
        paragraphLines.push(lines[index].trim());
        index += 1;
      }

      blocks.push({
        type: 'paragraph',
        tokens: this.parseInline(paragraphLines.join(' '), displayMaths),
      });
    }

    return blocks;
  }

  private isTableRow(trimmed: string): boolean {
    if (!trimmed.includes('|')) {
      return false;
    }

    return this.splitTableCells(trimmed).length >= 2;
  }

  private isTableSeparator(trimmed: string): boolean {
    return /^\|?\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)+\|?$/.test(trimmed);
  }

  private splitTableCells(trimmed: string): string[] {
    let row = trimmed;
    if (row.startsWith('|')) {
      row = row.slice(1);
    }
    if (row.endsWith('|')) {
      row = row.slice(0, -1);
    }
    return row.split('|').map((cell) => cell.trim());
  }

  private isParagraphContinuation(lines: string[], index: number): boolean {
    const trimmed = lines[index].trim();
    const isTableStart =
      this.isTableRow(trimmed) &&
      index + 1 < lines.length &&
      this.isTableSeparator(lines[index + 1].trim());

    return (
      !!trimmed &&
      !isTableStart &&
      !trimmed.startsWith('```') &&
      !trimmed.startsWith('#') &&
      !trimmed.startsWith('> ') &&
      !/^[-*]\s+/.test(trimmed) &&
      !/^\d+[.)]\s+/.test(trimmed) &&
      !/^---+$/.test(trimmed) &&
      !/^\x00MATH\d+\x00$/.test(trimmed)
    );
  }

  private parseInline(text: string, displayMaths: string[]): InlineToken[] {
    const tokens: InlineToken[] = [];
    let index = 0;

    while (index < text.length) {
      if (text.startsWith('\x00MATH', index)) {
        const end = text.indexOf('\x00', index + 6);

        if (end > index) {
          const numStr = text.slice(index + 6, end);
          const mathIndex = parseInt(numStr, 10);
          const latex = displayMaths[mathIndex];

          if (latex) {
            tokens.push({ type: 'math', html: this.renderMath(latex, true) });
          }

          index = end + 1;
          continue;
        }
      }

      if (text[index] === '[') {
        const closeBracket = text.indexOf(']', index + 1);
        if (closeBracket > index + 1 && text[closeBracket + 1] === '(') {
          const closeParen = text.indexOf(')', closeBracket + 2);
          if (closeParen > closeBracket + 2) {
            const label = text.slice(index + 1, closeBracket);
            const rawUrl = text.slice(closeBracket + 2, closeParen).trim();
            if (this.isSafeLinkUrl(rawUrl)) {
              tokens.push({ type: 'link', text: label, url: rawUrl });
              index = closeParen + 1;
              continue;
            }
          }
        }
      }

      if (text[index] === '$' && text[index + 1] !== '$') {
        const end = this.findInlineMathEnd(text, index);

        if (end > index + 1) {
          const mathContent = text.slice(index + 1, end);
          tokens.push({ type: 'math', html: this.renderMath(mathContent, false) });
          index = end + 1;
          continue;
        }
      }

      if (text.startsWith('$$', index)) {
        index += 2;
        continue;
      }

      if (text[index] === '`') {
        const end = text.indexOf('`', index + 1);

        if (end > index) {
          tokens.push({ type: 'code', text: text.slice(index + 1, end) });
          index = end + 1;
          continue;
        }
      }

      if (text.startsWith('**', index)) {
        const end = text.indexOf('**', index + 2);

        if (end > index) {
          tokens.push({ type: 'bold', text: text.slice(index + 2, end) });
          index = end + 2;
          continue;
        }
      }

      if (text[index] === '*') {
        const end = text.indexOf('*', index + 1);

        if (end > index) {
          tokens.push({ type: 'italic', text: text.slice(index + 1, end) });
          index = end + 1;
          continue;
        }
      }

      const nextSpecial = this.nextSpecialIndex(text, index + 1);
      tokens.push({ type: 'text', text: text.slice(index, nextSpecial) });
      index = nextSpecial;
    }

    return tokens;
  }

  private isSafeLinkUrl(url: string): boolean {
    return /^https?:\/\/[^\s]+$/i.test(url) || /^mailto:[^\s]+$/i.test(url);
  }

  private findInlineMathEnd(text: string, openIndex: number): number {
    const firstInner = text[openIndex + 1];
    if (!firstInner || firstInner === '$' || /\s/.test(firstInner)) {
      return -1;
    }

    let searchFrom = openIndex + 2;
    while (searchFrom < text.length) {
      const candidate = text.indexOf('$', searchFrom);
      if (candidate < 0) {
        return -1;
      }

      if (text[candidate - 1] === '\\') {
        searchFrom = candidate + 1;
        continue;
      }

      const lastInner = text[candidate - 1];
      const afterClose = text[candidate + 1] ?? '';

      if (!/\s/.test(lastInner) && afterClose !== '$' && !/\d/.test(afterClose)) {
        return candidate;
      }

      return -1;
    }

    return -1;
  }

  private nextSpecialIndex(text: string, start: number): number {
    const indexes = ['\x00', '`', '*', '$', '[']
      .map((character) => text.indexOf(character, start))
      .filter((index) => index >= 0);

    return indexes.length > 0 ? Math.min(...indexes) : text.length;
  }
}
