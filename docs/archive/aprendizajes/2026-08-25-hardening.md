# Aprendizajes — 2026-08-25

## Tema: Sprints de hardening — prompt y persistencia

### 1. El Tracker alucina persistencia (H-001, H-003)

**Lo que pasó:** Tanto el tutor de Programación (sesión 6, H-001) como el de
JavaScript (sesión 8, H-003) afirmaron registrar memoria sin hacerlo. En la
sesión 8, hubo 11 mensajes de contenido real pero solo 1 MemoryChange (`Set
/alias`); `memoria_sesion`, `mapa_dominio` y `lagunas_o_errores` quedaron
vacíos. No hay logs disponibles (API lanzada por usuario sin captura a archivo).

**Patrón:** El LLM tiende a "completar" la secuencia lógica (abrir → trabajar →
cerrar con registro) sin verificar que la tool `guardar_memoria` fue llamada
exitosamente. Es un sesgo de completitud, no un bug de código.

**Mitigación (prompt, aplicada):** Se añadió regla en `PROMPT_GLOBAL.md`:
"Nunca afirmes al usuario que algo fue registrado si no llamaste
`guardar_memoria` exitosamente en esto turno." Aplica a todos los roles,
incluido el Tracker. La tool `listar_memoria` ya existía pero nunca se
instruyó para verificación; la regla la hace innecesaria para el caso
común.

**Segunda capa (pendiente):** Detectar en código cuando el tutor declara
persistencia en texto sin que hubo `guardar_memoria` exitoso, e inyectar
una nota de corrección al LLM.

---

### 2. Prompt improvements: tema puente, diagnóstico pre-tema, verificación (H-008, H-009, H-010)

**Lo que pasó:** En la sesión 8 de JavaScript, el tutor transitó de prototipos
directamente a Event Loop sin tema puente, cerró var/let/const sin verificación
autónoma, y no hizo diagnóstico pre-tema para calibrar conocimiento previo.

**Cambios aplicados** en `PROMPT_GLOBAL.md`:
- **Tema puente obligatorio** (H-008): nueva regla en "Reglas inquebrantables"
  que obliga a reforzar conocimiento previo antes de saltar entre bloques
  conceptualmente distantes.
- **Verificación antes de cierre** (H-009): se extendió la regla existente
  "requiere evaluación autónoma aprobada" con: "nunca marques un tema como
  'corregido' o 'avanzado' solo porque el estudiante respondió bien a tu
  explicación".
- **Diagnóstico pre-tema** (H-010): nueva regla que exige pregunta breve de
  1-2 oraciones antes de introducir un tema nuevo.

**Esfuerzo:** Bajo (3 inserciones en el `.md`). **Impacto:** Alto — el prompt
ya es la herramienta de mayor palanca para calidad pedagógica.

---

### 3. Backlog completo de hallazgos (hallazgos-hardening.md)

**Lo que se hizo:** Se consolidaron todos los hallazgos acumulados en un solo
archivo con 12 hallazgos (H-001 a H-012, D-001), cada uno con estado,
evidencia y opciones de mitigación. Se priorizó por riesgo/costo:
1. Prompt (ya resuelto: H-008, H-009, H-010)
2. Persistencia (pendiente: H-001, H-003)
3. Semilla (pendiente: H-006, H-007)
4. Concurrencia (post-MVP: H-002)
5. Goal inyectado (decisión pendiente: D-001)

---

### 4. Decisión de diseño: StudySession.Goal (D-001)

**Lo que se decidió:** `StudySession.Goal` se mantiene como informativo, no
imperativo. No se inyecta en prompts hoy. Si se inyecta en el futuro, será
subordinado a `objetivo_declarado` del perfil y editable a mitad de sesión.

**Registrado en:** `decisiones.md`.

---

### 5. Validación E2E sesión 9 — Prompt improvements funcionando

**Lo que pasó:** Se ejecutó una sesión completa (34 mensajes) con tutor 7 (Dev Master, `gemini-3.1-flash-lite`) repasando prototipos y closures.

**Resultado:** Todas las 4 reglas nuevas funcionaron:
- **H-010 (Diagnóstico pre-tema):** Pregunta inicial calibrando conocimiento antes de Capa 1
- **H-008 (Tema puente):** Transición prototipos → closures con pregunta diagnóstica y explicación técnica de `this` vs closure ("ortogonales")
- **H-009 (Verificación antes de cierre):** Ejercicio `delete` para probar delegación dinámica; ejercicio de trade-off closure/prototipo
- **H-001/H-003 (Tracker persistencia):** Tracker llamó `guardar_memoria` 4 veces (4 MemoryChanges) y mostró los patches en la respuesta — **ya no alucina**

**Memoria registrada:** `memoria_sesion` con `temas_dominados_ultima_sesion`, `fecha_ultima_sesion`, `proximo_paso`; `perfil_estudiante` actualizado. Mejora drástica vs sesión 8 (11 mensajes, 1 MemoryChange).

---

## Siguientes pasos

1. ~~H-001/H-003~~ → **Resuelto (validado sesión 9)**
2. ~~H-006/H-007~~ → **Resuelto** (`DetectDomainMapJson` en `MemoryEntryDefaults`)
3. ~~H-008/H-009/H-010~~ → **Resuelto** (prompt improvements + validado E2E)
4. **H-006 (semilla):** Sincronizar prompts embebidos en la migración seed con los `.md` actuales (bajo esfuerzo, baja prioridad).
5. **H-002 (concurrencia):** Post-MVP — alto esfuerzo, bajo impacto actual.
6. **D-001 (Goal inyectado):** Decisión pendiente de valor.

> **Backlog efectivo vacío de items críticos.** Todo lo prioritario (prompt quality + persistencia) está resuelto y validado E2E.
