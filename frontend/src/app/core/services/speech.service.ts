import { HttpClient } from '@angular/common/http';
import { Injectable, OnDestroy, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { environment } from '../../../environments/environment';

export type TtsEngine = 'browser' | 'gemini';

export interface SpeakOptions {
  /** BCP-47 language tag, e.g. `en-US` or `es-MX`. */
  lang?: string;
  /** Playback rate (0.5 – 1.5). */
  rate?: number;
}

const engineStorageKey = 'synapse-tts-engine';
const maxCachedClips = 30;

/**
 * Text-to-speech facade. Uses the browser Web Speech API by default and can
 * optionally use Gemini TTS through `POST /api/tts`, falling back to the
 * browser voice whenever the backend call fails (quota, missing key, offline).
 */
@Injectable({ providedIn: 'root' })
export class SpeechService implements OnDestroy {
  private readonly http = inject(HttpClient);
  private readonly ttsUrl = `${environment.apiUrl}/api/tts`;
  private readonly synth: SpeechSynthesis | null =
    typeof globalThis.speechSynthesis !== 'undefined' ? globalThis.speechSynthesis : null;
  private readonly clipCache = new Map<string, string>();
  private audio: HTMLAudioElement | null = null;
  private playToken = 0;

  readonly browserSupported = this.synth !== null;
  readonly engine = signal<TtsEngine>(this.loadEngine());
  /** Id of the item currently playing (or being prepared). */
  readonly activeId = signal<string | null>(null);
  readonly loading = signal(false);
  readonly notice = signal<string | null>(null);

  constructor() {
    // Some browsers populate voices asynchronously; touching the list warms it up.
    this.synth?.getVoices();
  }

  ngOnDestroy(): void {
    this.stop();
    for (const url of this.clipCache.values()) {
      URL.revokeObjectURL(url);
    }
    this.clipCache.clear();
  }

  setEngine(engine: TtsEngine): void {
    this.engine.set(engine);
    this.notice.set(null);
    if (typeof localStorage !== 'undefined') {
      localStorage.setItem(engineStorageKey, engine);
    }
  }

  isActive(id: string): boolean {
    return this.activeId() === id;
  }

  toggle(id: string, text: string, options: SpeakOptions = {}): void {
    if (this.activeId() === id) {
      this.stop();
      return;
    }

    void this.speak(id, text, options);
  }

  async speak(id: string, text: string, options: SpeakOptions = {}): Promise<void> {
    const content = text.trim();
    if (!content) {
      return;
    }

    this.stop();
    const token = ++this.playToken;
    this.activeId.set(id);
    this.notice.set(null);

    const rate = clamp(options.rate ?? 1, 0.5, 1.5);
    const lang = options.lang ?? 'es-MX';

    if (this.engine() === 'gemini') {
      const played = await this.playWithGemini(token, content, rate);
      if (played || token !== this.playToken) {
        return;
      }

      this.notice.set('Voz Gemini no disponible en este momento; usando la voz del navegador.');
    }

    this.playWithBrowser(token, content, lang, rate);
  }

  stop(): void {
    this.playToken++;
    this.synth?.cancel();
    if (this.audio) {
      this.audio.onended = null;
      this.audio.onerror = null;
      this.audio.pause();
      this.audio = null;
    }
    this.loading.set(false);
    this.activeId.set(null);
  }

  private playWithBrowser(token: number, text: string, lang: string, rate: number): void {
    if (!this.synth) {
      this.notice.set('Tu navegador no soporta lectura en voz alta.');
      this.activeId.set(null);
      return;
    }

    const utterance = new SpeechSynthesisUtterance(text);
    utterance.lang = lang;
    utterance.rate = rate;
    const voice = this.pickVoice(lang);
    if (voice) {
      utterance.voice = voice;
    }

    utterance.onend = () => this.finish(token);
    utterance.onerror = () => this.finish(token);
    this.synth.speak(utterance);
  }

  private async playWithGemini(token: number, text: string, rate: number): Promise<boolean> {
    try {
      this.loading.set(true);
      const url = await this.getGeminiClipUrl(text);
      if (token !== this.playToken) {
        return true;
      }

      const audio = new Audio(url);
      audio.playbackRate = rate;
      audio.onended = () => this.finish(token);
      audio.onerror = () => this.finish(token);
      this.audio = audio;
      this.loading.set(false);
      await audio.play();
      return true;
    } catch {
      if (token === this.playToken) {
        this.loading.set(false);
      }
      return false;
    }
  }

  private async getGeminiClipUrl(text: string): Promise<string> {
    const cached = this.clipCache.get(text);
    if (cached) {
      return cached;
    }

    const blob = await firstValueFrom(this.http.post(this.ttsUrl, { text }, { responseType: 'blob' }));
    const url = URL.createObjectURL(blob);
    this.clipCache.set(text, url);

    if (this.clipCache.size > maxCachedClips) {
      const oldestKey = this.clipCache.keys().next().value as string;
      URL.revokeObjectURL(this.clipCache.get(oldestKey)!);
      this.clipCache.delete(oldestKey);
    }

    return url;
  }

  private finish(token: number): void {
    if (token === this.playToken) {
      this.activeId.set(null);
      this.loading.set(false);
      this.audio = null;
    }
  }

  private pickVoice(lang: string): SpeechSynthesisVoice | null {
    const voices = this.synth?.getVoices() ?? [];
    if (voices.length === 0) {
      return null;
    }

    const target = lang.toLowerCase();
    const prefix = target.split('-')[0];
    const exact = voices.filter((v) => v.lang.toLowerCase() === target);
    const sameLanguage = voices.filter((v) => v.lang.toLowerCase().startsWith(prefix));
    const pool = exact.length > 0 ? exact : sameLanguage;
    if (pool.length === 0) {
      return null;
    }

    // Prefer neural/online voices (Edge "Natural", Chrome "Google") for clearer listening practice.
    return (
      pool.find((v) => /natural|online|neural/i.test(v.name)) ??
      pool.find((v) => /google/i.test(v.name)) ??
      pool[0]
    );
  }

  private loadEngine(): TtsEngine {
    if (typeof localStorage === 'undefined') {
      return 'browser';
    }

    return localStorage.getItem(engineStorageKey) === 'gemini' ? 'gemini' : 'browser';
  }
}

function clamp(value: number, min: number, max: number): number {
  return Number.isFinite(value) ? Math.min(max, Math.max(min, value)) : 1;
}

/**
 * Converts tutor Markdown into plain text suitable for speech synthesis:
 * drops diagrams and code blocks, strips formatting marks and role tags.
 */
export function markdownToSpeechText(markdown: string): string {
  return markdown
    .replace(/\r\n/g, '\n')
    .replace(/```mermaid[\s\S]*?(```|$)/g, ' ')
    .replace(/```listening[^\n]*\n([\s\S]*?)(```|$)/g, (_m, inner: string) => ` ${inner} `)
    .replace(/```[\s\S]*?(```|$)/g, ' (bloque de código) ')
    .replace(/\$\$[\s\S]*?\$\$/g, ' (fórmula) ')
    .replace(/\[([^\]]+)\]\((https?:\/\/|mailto:)[^)]+\)/g, '$1')
    .replace(/^\s*\[[A-Za-zÁÉÍÓÚáéíóúñÑ ]{2,30}\]\s*/gm, '')
    .replace(/^\s*\|?\s*:?-{3,}.*$/gm, ' ')
    .replace(/[|#>*_`$]/g, ' ')
    .replace(/^\s*[-•]\s+/gm, '')
    .replace(/[ \t]+/g, ' ')
    .replace(/\n{2,}/g, '\n')
    .trim();
}
