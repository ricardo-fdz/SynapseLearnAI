# Backlog — Hallazgos activos

> Single source del backlog. Resueltos movidos a `archive/hallazgos-resueltos.md`. Ver `README.md` para índice.

## H-002 — Sin control de concurrencia en memoria

**Estado:** Documentado | **Esfuerzo:** Alto | **Prioridad:** Media | **Fecha:** 2026-08-12

Memoria a nivel **Tutor** compartida entre sesiones. Sin `RowVersion`/`SemaphoreSlim`. Escenario: dos sesiones del mismo tutor leen `mapa_dominio` y hacen `Add` concurrentes → last-write-wins no determinista. `ConversationService.cs` guard anti-falso-éxito protege `targetId` intra-turno, no atomicidad inter-turno.

Opciones: `RowVersion` + `DbUpdateConcurrencyException`, `SemaphoreSlim` por `TutorId`, re-leer `UpdatedAtUtc` antes de patch, o documentar last-write-wins para MVP (actual).

## H-011 — `siguiente_tema` vs `proximo_paso` (campo diferente)

**Estado:** Documentado | **Esfuerzo:** Bajo | **Prioridad:** Baja | **Fecha:** 2026-08-25

Tutor escribió `proximo_paso` en vez de `siguiente_tema`. Ambos válidos en `MemoryToolDeclarations.cs`, pero `PromptBuilder.cs:152` solo renderiza `siguiente_tema` en prompt. Si solo escribe `proximo_paso`, no se reinyecta.

Opciones: renderizar ambos en `PromptBuilder`, o consolidar a un campo.

## H-012 — Gemini HTTP 400 intermitente en mensajes largos

**Estado:** Documentado | **Prioridad:** Baja | **Fecha:** 2026-08-12

Mensajes largos vía `send.py` fallan 400 no determinista; cortos OK. No afecta usuario final (frontend chunkera), pero dificulta pruebas automatizadas. Opción: no fix de código (proveedor), chunkear en tests.

## D-001 — `StudySession.Goal` no se inyecta en prompts (diseño informativo)

**Estado:** Decidido (pendiente implementación) | **Esfuerzo:** Medio | **Prioridad:** Baja | **Fecha:** 2026-08-12

`Goal` persiste pero no va al prompt; tutor usa `perfil_estudiante.objetivo_declarado`. Decisión `decisiones.md`: si se inyecta, como guía subordinada y editable. Pendiente `PromptBuilder`.

---
> **Nota:** H-013 escalado prompt ya resuelto (cap 30 msgs, `PromptBuilder` truncado) — ver `archive/hallazgos-resueltos.md`.
