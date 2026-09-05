# Backlog — Hallazgos activos

> Single source del backlog. Resueltos movidos a `archive/hallazgos-resueltos.md`. Ver `README.md` para índice.

> **2026-09-05:** H-002, H-011, D-001, H-012 y H-013 resueltos. **Backlog activo vacío** — todo lo priorizado cerrado. Ver `archive/hallazgos-resueltos.md` para histórico.

## H-012 — Gemini HTTP 400 intermitente en mensajes largos

**Estado:** Cerrado (won't fix) | **Prioridad:** Baja | **Fecha:** 2026-08-12

Mensajes largos vía `send.py` fallan 400 no determinista; cortos OK. No afecta usuario final (frontend chunkera). Mitigación: H-013 cap 30 msgs + truncado `historial_actividades` reduce payload; chunkear en tests. Sin fix de código proveedor.

## D-001 — `StudySession.Goal` no se inyecta en prompts (diseño informativo)

**Estado:** Resuelto | **Esfuerzo:** Medio | **Prioridad:** Baja | **Fecha:** 2026-09-05

Implementado `PromptBuilder.cs:40` overload con `sessionGoal` + `ConversationService.cs:72` pasa `session.Goal` como guía subordinada: `Meta declarada de esta sesión: "X". Es una guía, no un límite...`. 48/48 tests OK. Ver `decisiones.md:26`.

---
> **Nota:** H-013 escalado prompt ya resuelto (cap 30 msgs, `PromptBuilder` truncado) — ver `archive/hallazgos-resueltos.md`.
