import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, Input, OnChanges, inject, signal } from '@angular/core';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import katex from 'katex';

type InlineToken =
  | { type: 'text'; text: string }
  | { type: 'bold'; text: string }
  | { type: 'italic'; text: string }
  | { type: 'code'; text: string }
  | { type: 'math'; html: SafeHtml };

type MarkdownBlock =
  | { type: 'paragraph'; tokens: InlineToken[] }
  | { type: 'heading'; level: 1 | 2 | 3; tokens: InlineToken[] }
  | { type: 'quote'; tokens: InlineToken[] }
  | { type: 'unordered-list'; items: InlineToken[][] }
  | { type: 'ordered-list'; items: InlineToken[][] }
  | { type: 'code-block'; text: string }
  | { type: 'rule' }
  | { type: 'math'; html: SafeHtml };

@Component({
  selector: 'app-markdown-message',
  imports: [NgTemplateOutlet],
  templateUrl: './markdown-message.component.html',
  styleUrl: './markdown-message.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MarkdownMessageComponent implements OnChanges {
  @Input({ required: true }) content = '';

  readonly blocks = signal<MarkdownBlock[]>([]);

  private readonly sanitizer = inject(DomSanitizer);

  ngOnChanges(): void {
    this.blocks.set(this.parseBlocks(this.content));
  }

  private renderMath(latex: string, display: boolean): SafeHtml {
    try {
      const html = katex.renderToString(latex, {
        throwOnError: false,
        displayMode: display,
      });
      return this.sanitizer.bypassSecurityTrustHtml(html);
    } catch {
      return this.sanitizer.bypassSecurityTrustHtml(`<code>${latex}</code>`);
    }
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
        const codeLines: string[] = [];
        index += 1;

        while (index < lines.length && !lines[index].trim().startsWith('```')) {
          codeLines.push(lines[index]);
          index += 1;
        }

        blocks.push({ type: 'code-block', text: codeLines.join('\n') });
        index += 1;
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

      while (index < lines.length && this.isParagraphContinuation(lines[index])) {
        paragraphLines.push(lines[index].trim());
        index += 1;
      }

      blocks.push({ type: 'paragraph', tokens: this.parseInline(paragraphLines.join(' '), displayMaths) });
    }

    return blocks;
  }

  private isParagraphContinuation(line: string): boolean {
    const trimmed = line.trim();

    return (
      !!trimmed &&
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

      if (text[index] === '$' && text[index + 1] !== '$') {
        const end = text.indexOf('$', index + 1);

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

  private nextSpecialIndex(text: string, start: number): number {
    const indexes = ['\x00', '`', '*', '$']
      .map((character) => text.indexOf(character, start))
      .filter((index) => index >= 0);

    return indexes.length > 0 ? Math.min(...indexes) : text.length;
  }
}
