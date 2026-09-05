# Hallazgos de robustez — backlog completo

Lista de hallazgos observados en pruebas reales (flujo interactivo contra la API)
que van desde bugs de código hasta fricciones de diseño del prompt o la
arquitectura pedagógica del agente. Cada hallazgo se anota una sola vez con
fecha, evidencia y opciones de mitigación.

---

## Estado

- **Documentado** = registrado, sin decidir ni implementar
- **En investigación** = se está evaluando la mejor mitigación
- **Decidido** = se tomó la decisión de diseño, pendiente de implementación
- **Resuelto** = mitigación aplicada y verificada
- **Backlog** = pendiente de decisión/Implementación

---

<!-- Agregar nuevos hallazgos debajo de esta línea -->

---

## PERSISTENCIA Y MEMORIA

### H-001 — El Tracker "alucina" persistencia: afirma registrar memoria sin llamar la tool

**Estado:** Resuelto (validado en sesión 9)
**Fecha:** 2026-08-12

**Contexto:** Proyecto integrador "Resilient Data Pipeline" (sesión 6, tutor de
Programación, perfil `gemini-3.1-flash-lite`). Tras completar las 4 fases y veredicto
del Arquitecto, el rol `[Tracker]` cerró la sesión con: *"historial_actividades:
Proyecto 'Resilient Data Pipeline' registrado como completado con éxito"*.

**Evidencia:** La entrada `historial_actividades` del tutor quedó en `{"proyectos":[]}`
y el último MemoryChange real fue el #62 (`Add /temas`). No existió ninguna llamada
`guardar_memoria` con `Add /proyectos` (verificado contra el log del API y
`/api/tutors/6/memory-changes`). El texto del Tracker describe un registro que el
sistema jamás aplicó.

**Por qué importa:** Rompe la confianza del MVP en que "lo que el tutor dice que
recuerda, está en memoria". Un usuario puede creer que su proyecto/actividad quedó
guardado cuando no es así.

**Opciones de hardening (sin decidir aún):**
- Verificar/reconciliar claims de persistencia del Tracker comparando el texto de
  cierre contra las MemoryChanges recientes de ese mensaje.
- Tras un mensaje del tutor que describa un registro, forzar (en prompt del rol
  Tracker) un `listar_memoria` + confirmación explícita antes de afirmarlo al usuario.
- Instrumentar el flujo para que el cliente marque cuando el tutor *declara*
  persistencia pero no hubo ningún `guardar_memoria` exitoso en ese turno.
- Test de humo E2E dedicado: pedir al tutor "cierra registrando el proyecto en
  historial_actividades" y assert de que `historial_actividades` deje de ser vacío.

**Relación:** Conflicto A (modelo omite `Add /habilidades`) era el mismo síntoma con
distinto origen (binarios v1 + esquema sin `reason`); ambos resueltos en sesiones
5-6. H-001 es más grave: no hubo ni siquiera intento de tool call.

---

### H-002 — Sin control de concurrencia en memoria: dos sesiones del mismo tutor pueden perder/sobrescribir escrituras

**Estado:** Documentado
**Fecha:** 2026-08-12

**Contexto:** La memoria vive a nivel **Tutor** (no por sesión); dos sesiones del
mismo tutor comparten la misma `MemoryEntry`. No existe ningún mecanismo de
concurrencia: sin `RowVersion`/`IsConcurrencyToken` en `MemoryEntry`, sin lock
por `TutorId`, sin control optimista.

**Escenario de fallo:** Sesión A lee `mapa_dominio` (versión vieja), B lo lee
simultáneamente, ambos hacen `Add` parciales. El último en guardar gana de forma
no determinista (last-write-wins); un `Add` puede pisarse.

**Por qué importa:** Probable bajo uso típico (single-user, sesiones secuenciales),
pero falla real si se abren dos sesiones del mismo tutor a la vez.

**Opciones de hardening (sin decidir aún):**
- Control de concurrencia optimista: `RowVersion` + `DbUpdateConcurrencyException`.
- SemaphoreSlim por `TutorId` para serializar escrituras.
- Re-leer y comparar `UpdatedAtUtc` justo antes de aplicar el patch.
- Documentar política: aceptar last-write-wins para el MVP.

**Relación:** Complementario al guard anti-falso-éxito (leer-antes-de-escribir);
ese guard protege `targetId` dentro de un turno, no la atomicidad entre turnos.

---

### H-003 — Tutor de JS (sesión 8) no registró memoria en 11 mensajes de contenido real

**Estado:** Resuelto (validado en sesión 9)
**Fecha:** 2026-08-25

**Contexto:** Sesión 8 "Repaso JavaScript" (tutor 7, `gemini-3.1-flash-lite`).
El usuario validó progresión pedagógica, no el registro de memoria. La sesión
tuvo 11 mensajes con contenido sustantivo (diagnóstico de scope, prototipos,
copia de objetos, `getOwnPropertyNames`), pero:

**Evidencia:**
- `memoria_sesion`: `{}` vacío
- `mapa_dominio`: `{"temas": []}`
- `lagunas_o_errores`: vacío
- `MemoryChanges`: solo 1 registro (`Set /alias`, id 63)
- No hay logs disponibles (API lanzada por usuario sin captura a archivo)

**Por qué importa:** Es una recurrencia de H-001 en contexto diferente. Sin
registros, la próxima sesión del mismo tutor empezará de cero sin conocer los
temas cubierto ni las lagunas del estudiante (scope confundido, prototipos
con errores con `for...in`).

**Opciones de hardening (sin decidir aún):**
- Misma que H-001: verificar claims de persistencia del tutor vs reales.
- "Pulse check": si hay más de N mensajes sin ningún `MemoryChange` exitoso,
  marcar como anomalía.
- Forzar en prompt: "debes registrar el estado de la sesión al menos cada
  3 intercambios".

**Relación:** Misma familia que H-001; refuerza que el problema de
"alucinar persistencia" ocurre de forma transversal, no solo en el Tracker.

---

## CONCURRENCIA Y CONFIABILIDAD

### H-004 — Binarios v1 del API en ejecución causaron fallos de persistencia (ya resuelto)

**Estado:** Resuelto
**Fecha:** 2026-08-12

**Contexto:** El API corría binarios compilados a las 15:19 (antes de los cambios
v2 a las 18:24). El proceso fue lanzado a las 16:48. El engine viejo solo
aceptaba `/temas`; los intentos de `Add /habilidades` fallaban con "path not
supported".

**Resolución:** Reconstruido y relanzado con binarios v2. Verificado con flujo
completo de 3 temas (tutores 5 y 6).

---

### H-005 — Esquema JSON sin `reason` en required causó omisión masiva de tool calls (ya resuelto)

**Estado:** Resuelto
**Fecha:** 2026-08-12

**Contexto:** El esquema JSON de `guardar_memoria` declaraba `required:
["key", "operation", "path", "value"]` sin `reason`. El modelo
(`gemini-3.1-flash-lite`) omitía `reason` en llamadas paralelas; el handler
`ParsePatch` lo rechazaba con "Argument 'reason' is required."

**Resolución:** Añadido `reason` al array `required` en
`MemoryToolDeclarations.cs`. Verificado con diagnóstico completo del tutor 5
(6 MemoryChanges registradas sin errores).

---

## DISEÑO Y SEMILLA

### H-006 — Migración seed con prompts embebidos desalineados de los .md actuales

**Estado:** Resuelto
**Fecha:** 2026-08-12

**Contexto:** La migración `20260811211809_SeedEnglishAndTutorPrompts.cs` embebe
el contenido de los prompts de programación e inglés como strings literales.
Actualizamos los `.md` y `mapa_dominio` en la migración, pero los prompts
embebidos siguen siendo las versiones antiguas.

**Por qué importa:** Una base de datos nueva creada desde cero (sin API) tendría
los prompts viejos en los tutores seed. Los tutores creados por API sí usan el
contenido actual de los `.md`.

**Resolución:** Se creó migración `20260827011302_SyncSeedPrompts` con Designer
sincronizado al estado actual de la DB. El Up está vacío porque los tutores
seed ya tienen los prompts actuales (actualizados vía API). Para DBs nuevas,
el Designer refleja el estado correcto.

**Opciones de hardening:**
- ✅ Migración sync creada (Up vacío, Designer correcto).
- Alternativa: load prompts desde `.md` en runtime (ya hace PromptBuilder).

---

### H-007 — `MemoryEntryDefaults.CreateForTutor` siempre semilla `{"temas":[]}` para todos los tutores

**Estado:** Resuelto
**Fecha:** 2026-08-12

**Contexto:** `MemoryEntryDefaults.DomainMapJson = "{\"temas\":[]}"` se usa para
todos los tutores, incluidos los de idiomas que deberían usar `"habilidades"`.

**Impacto:** En sesiones de prueba con tutores de idiomas, el engine
(`GetOrCreateArray`) auto-crea `habilidades` en el primer `Add`, pero el
estado inicial es `{"temas":[]}`. Cada tutor nuevo de idiomas requiere
edición manual vía Python para corregirlo (como hicimos con tutores 3-5).

**Opciones de hardening:**
- Detectar tipo de tutor por prompt contenido (buscar "idioma"/"english") y
  semilla condicionalmente `habilidades` vs `temas`.
- Parametrizar `CreateForTutor` con un string de default de mapa.
- Dejar como está (el engine compensa, es solo estético en el primer `Add`).

---

## CALIDAD PEDAGÓGICA DEL PROMPT

### H-008 — Falta tema puente entre prototipos y Event Loop (closures/`this`)

**Estado:** Resuelto
**Fecha:** 2026-08-25

**Contexto:** En la sesión 8 (Repaso JS), el tutor transitó directamente de
"Prototipos y Copia de Objetos" a "Event Loop y Async" sin tema intermedio.
Ricardo mostró dudas en scope (var/let/const) y prototipos (`for...in` vs
`getOwnPropertyNames`), lo que sugiere que closures y `this` binding no
están consolidados.

**Por qué importa:** Closures conecta scope con callbacks, y `this` binding
es prerequisito para entender funciones asíncronas. Sin ese puente, el
tutor asume que el estudiante ya maneja conceptos que no verificó.

**Opciones de hardening (prompt):**
- Agregar en `PROMPT_GLOBAL.md` la regla: "Al transitar entre bloques de
  temas conceptualmente distantes, propone un tema puente corto que refuerce
  el conocimiento previo antes de avanzar."
- Mantener un mapa estático de dependencias por dominio (ej. JS:
  scope → closures → this → callbacks → async).

---

### H-009 — Cierre sin confirmación de temas (var/let/const marcado como "corregido" sin verificación)

**Estado:** Resuelto
**Fecha:** 2026-08-25

**Contexto:** En la sesión 8, el tutor explicó hoisting/scope para var/let/const
(msg 203), Ricardo dijo "ok", y el tutor avanzó a prototipos sin una pregunta
de verificación nueva. A diferencia del segundo tema (prototipos), donde sí hubo
un "hazlo ahora" que generó un error real.

**Por qué importa:** Sin verificación, el mapa de dominio puede tener un tema
marcado como "nivel 3" cuando en realidad el estudiante solo escuchó la
explicación sin demostrar comprensión autónoma.

**Opciones de hardening (prompt):**
- Regla explícita: "Nunca marques un tema como dominado sin un ejercicio de
  verificación nuevo donde el estudiante demuestre comprensión autónoma."
- Forzar un formato: explicación → práctica → verificación → cierre.
- En el prompt de evaluación, exigir: "Antes de avanzar, confirma con una
  pregunta abierta o un ejercicio mínimo."

---

### H-010 — Falta diagnóstico pre-tema antes de introducir temas nuevos

**Estado:** Resuelto
**Fecha:** 2026-08-25

**Contexto:** En la sesión 8, el tutor propuso Event Loop directamente (msg 211)
sin preguntar qué sabe ya Ricardo sobre callbacks/promesas.

**Por qué importa:** El diagnóstico pre-tema permite calibrar: si el estudiante
ya maneja callbacks, puede saltar a macrotask/microtask; si no, necesita un
escalón intermedio. Sin esto, el tutor puede sobre-explicar o sub-explicar.

**Opciones de hardening (prompt):**
- Regla: "Antes de introducir un tema nuevo, haz una pregunta diagnóstica
  rápida de 1-2 oraciones para calibrar el punto de partida."
- Esto ya existe en el primer diagnóstico (Fase 1 del prompt), pero no se
  aplica a transiciones dentro de la sesión.

---

## DECISIÓN DE DISEÑO DOCUMENTADA

### D-001 — `StudySession.Goal` no se inyecta en prompts (diseño informativo)

**Estado:** Decidido (pendiente de implementación)
**Fecha:** 2026-08-12

**Contexto:** `StudySession.Goal` se persiste pero no se usa en ningún prompt.
El objetivo que el tutor conoce viene de `perfil_estudiante.objetivo_declarado`.

**Decisión (ver `decisiones.md`):** Goal informativo, no imperativo:
- Si se inyecta, será como contexto ("Meta de la sesión: X. Es una guía.").
- Subordinado a `objetivo_declarado` del perfil.
- Editable a mitad de sesión.

**Pendiente:** Decidir si implementar la inyección en `PromptBuilder`.

---

## HALLAZGOS DE PRIORIDAD BAJA (no críticos, backlog)

### H-011 — `siguiente_tema` en memoria se escribió como `proximo_paso` (campo diferente)

**Estado:** Documentado
**Fecha:** 2026-08-25

**Contexto:** El tutor de Inglés (sesión 3, binarios viejos) escribió `proximo_paso`
en `memoria_sesion` en vez de `siguiente_tema`. El campo `proximo_paso` sí es
válido (declarado en `MemoryToolDeclarations.cs`), pero la distinción entre
ambos es sutil: `siguiente_tema` guarda el ID del tema del mapa, `proximo_paso`
la acción concreta.

**Impacto:** Bajo. Ambos campos existen y el prompt los lista. Pero el
`PromptBuilder` solo renderiza `siguiente_tema` en el prompt, así que si el
tutor solo escribe `proximo_paso`, ese dato no se reinyecta en el prompt
de la siguiente sesión.

**Opciones de hardening:**
- Renderizar ambos campos en `PromptBuilder`.
- O renombrar/consolidar a un solo campo.

---

### H-012 — Gemini API produce HTTP 400 intermitente en mensajes largos

**Estado:** Documentado
**Fecha:** 2026-08-12

**Contexto:** Durante las pruebas, mensajes largos enviados vía `send.py`
 fallaron consistentemente con HTTP 400, mientras que mensajes cortos
funcionaban. No fue determinístico (algunos mensajes largos funcionaron).

**Impacto:** Transitorio, no afecta al usuario final (el frontend construye
los payloads). Pero dificulta las pruebas automatizadas.

**Opciones:**
- No requiere fix de código; es comportamiento del proveedor LLM.
- Para pruebas, chunkear mensajes largos.

---

### H-013 — Escalado del prompt: historia y memoria sin acotar crecen linealmente por turno

**Estado:** Resuelto
**Fecha:** 2026-09-04

**Contexto:** `ConversationService.cs:37-42` cargaba **todo** el historial de la sesión (`Where SessionId == id OrderBy Id`) y `PromptBuilder.cs:47-61` renderizaba `historial_actividades` completo sin límite. Cada turno reenviaba `systemPrompt` (~12-15KB fijo) + historia completa + memoria (15-20KB en sesiones largas → ~11k tokens). Sin observabilidad.

**Evidencia (medición):** PROMPT_GLOBAL 10.2KB + PROMPT_PROGRAMACION 3.8KB = 14KB fijos. Sesión 11 con 35 msgs = 28KB historia. `historial_actividades` con 10 proyectos ×800 chars = 8KB solo en esa clave. No hay `Take` ni truncado.

**Mitigación aplicada:**
- `ConversationService.cs:22` `MaxHistoryMessages = 30` + `OrderByDescending Take + Reverse` + log `[H-013] history truncated from X to 30` y `[H-013] Prompt metrics: systemPrompt=N chars, history=M/N msgs, totalInput~N chars (~tokens)`
- `PromptBuilder.cs:221` `MaxActivityHistoryItems = 5` + `MaxFieldChars = 500` — renderiza solo últimos 5, trunca campos largos con `…`, soporta clave `proyectos` además de `actividades`, log implícito vía longitud.

**Validación:**
- Tests `H013PromptScalingTests.cs`: 4 tests (truncado a 5, soporte `proyectos`, cap 30, no-truncado) — 48/48 OK
- Prueba viva sesión 12: 40 msgs → log `history truncated from 40 to 30`, `systemPrompt=14551 chars, history=30/40, totalInput~20843 chars (~5210 tokens)` — correcto.

**Pendiente:** Prompt caching de Gemini (systemPrompt estático) para recortar 70% costo fijo — posponer a optimización de costos.

---

## RESUMEN DE BACKLOG

| ID | Hallazgo | Estado | Esfuerzo | Prioridad |
|---|---|---|---|---|
| H-001 | Tracker alucina persistencia | Resuelto (validado) | — | — |
| H-002 | Sin concurrencia en memoria | Documentado | Alto | Media |
| H-003 | Tutor JS no registró en 11 msgs | Resuelto (validado) | — | — |
| H-004 | Binarios v1 (resuelto) | Resuelto | — | — |
| H-005 | Esquema sin reason (resuelto) | Resuelto | — | — |
| H-006 | Seed prompts desalineados | Resuelto | — | — |
| H-007 | CreateForTutor semilla "temas" | Resuelto | — | — |
| H-008 | Falta tema puente (closures) | Resuelto | — | — |
| H-009 | Cierre sin confirmación | Resuelto | — | — |
| H-010 | Falta diagnóstico pre-tema | Resuelto | — | — |
| D-001 | Goal no inyectado en prompts | Decidido | Medio | Baja |
| H-011 | siguiente_tema vs proximo_paso | Documentado | Bajo | Baja |
| H-012 | HTTP 400 intermitente LLM | Documentado | — | Baja |
| H-013 | Escalado del prompt (historia/memoria) | Resuelto | — | — |

### Recomendación de ejecución

1. **Prompt (H-009, H-010, H-008):** cambios de una línea en `PROMPT_GLOBAL.md`.
   Esfuerzo bajo, impacto alto en calidad pedagógica. Ejecutar primero.
2. **Persistencia (H-001, H-003):** implementar verificación de claims en el
   cliente o forzar `listar_memoria` en el prompt del Tracker. Medio esfuerzo.
3. **Semilla (H-006, H-007):** bajo esfuerzo pero bajo impacto (solo afecta
   tutores nuevos por seed, no por API).
4. **Concurrencia (H-002):** alto esfuerzo, bajo impacto actual (single-user).
   Posible post-MVP.
5. **Goal inyectado (D-001):** medio esfuerzo, decisión pendiente de valor.

---
