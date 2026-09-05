# Backlog — Hallazgos activos

> Single source del backlog. Resueltos movidos a `archive/hallazgos-resueltos.md`. Ver `README.md` para índice.

> **2026-09-05:** H-002 resuelto con `RowVersion` (`MemoryEntry.cs:12` + `LearningAgentsDbContext.cs:84` + `MemoryPatchEngine.cs:87` catch `DbUpdateConcurrencyException`), H-011 verificado como ya resuelto (`PromptBuilder.cs:152` renderiza ambos campos). Backlog activo ahora solo D-001 y H-012.

## H-012 — Gemini HTTP 400 intermitente en mensajes largos

**Estado:** Documentado | **Prioridad:** Baja | **Fecha:** 2026-08-12

Mensajes largos vía `send.py` fallan 400 no determinista; cortos OK. No afecta usuario final (frontend chunkera), pero dificulta pruebas automatizadas. Opción: no fix de código (proveedor), chunkear en tests.

## D-001 — `StudySession.Goal` no se inyecta en prompts (diseño informativo)

**Estado:** Decidido (pendiente implementación) | **Esfuerzo:** Medio | **Prioridad:** Baja | **Fecha:** 2026-08-12

`Goal` persiste pero no va al prompt; tutor usa `perfil_estudiante.objetivo_declarado`. Decisión `decisiones.md`: si se inyecta, como guía subordinada y editable. Pendiente `PromptBuilder`.

---
> **Nota:** H-013 escalado prompt ya resuelto (cap 30 msgs, `PromptBuilder` truncado) — ver `archive/hallazgos-resueltos.md`.
