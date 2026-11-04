import { DOCUMENT } from '@angular/common';
import {
  AfterViewInit,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  EventEmitter,
  HostListener,
  Input,
  OnDestroy,
  Output,
  ViewChild,
  inject,
} from '@angular/core';

@Component({
  selector: 'app-modal',
  templateUrl: './modal.component.html',
  styleUrl: './modal.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ModalComponent implements AfterViewInit, OnDestroy {
  @Input({ required: true }) title = '';
  @Input() closeOnBackdrop = true;
  @Output() closeModal = new EventEmitter<void>();

  @ViewChild('modalPortal') private modalPortal?: ElementRef<HTMLElement>;
  @ViewChild('dialogSection') private dialogSection?: ElementRef<HTMLElement>;
  @ViewChild('dialogBody') private dialogBody?: ElementRef<HTMLElement>;
  @ViewChild('closeBtn') private closeBtn?: ElementRef<HTMLButtonElement>;

  private readonly document = inject(DOCUMENT);
  private previouslyFocusedElement: HTMLElement | null = null;

  constructor() {
    const active = this.document.activeElement;
    this.previouslyFocusedElement = active instanceof HTMLElement ? active : null;
  }

  ngAfterViewInit(): void {
    const portalEl = this.modalPortal?.nativeElement;
    if (portalEl && this.document.body && portalEl.parentElement !== this.document.body) {
      this.document.body.appendChild(portalEl);
    }

    window.setTimeout(() => {
      const firstFormField = this.dialogBody?.nativeElement.querySelector<HTMLElement>(
        'input:not([disabled]), select:not([disabled]), textarea:not([disabled]), button:not([disabled])',
      );
      (firstFormField ?? this.closeBtn?.nativeElement)?.focus();
    });
  }

  ngOnDestroy(): void {
    this.modalPortal?.nativeElement.remove();
    this.previouslyFocusedElement?.focus?.();
  }

  @HostListener('document:keydown', ['$event'])
  onDocumentKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape') {
      event.preventDefault();
      event.stopPropagation();
      this.closeModal.emit();
      return;
    }

    if (event.key !== 'Tab') {
      return;
    }

    const container = this.dialogSection?.nativeElement;
    if (!container) {
      return;
    }

    const focusables = Array.from(
      container.querySelectorAll<HTMLElement>(
        'button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])',
      ),
    );
    if (focusables.length === 0) {
      return;
    }

    const first = focusables[0];
    const last = focusables[focusables.length - 1];
    const active = this.document.activeElement;

    if (event.shiftKey && (active === first || !container.contains(active))) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && (active === last || !container.contains(active))) {
      event.preventDefault();
      first.focus();
    }
  }

  onBackdropClick(): void {
    if (this.closeOnBackdrop) {
      this.closeModal.emit();
    }
  }
}
