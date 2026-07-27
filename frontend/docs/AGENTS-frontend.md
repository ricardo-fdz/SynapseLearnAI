# AGENTS.md -- Synapse Learn - AI Learning Platform

Este archivo es el contexto persistente del proyecto para agentes de código (OpenCode).
Léelo completo antes de generar o modificar código. Si una instrucción del usuario
contradice este archivo, pregunta antes de proceder.

El backend ya está terminado y documentado en su propio AGENTS.md. Este archivo
cubre exclusivamente el frontend Angular.

---

## 1. Qué es este proyecto

Interfaz Angular para la plataforma de aprendizaje asistido. Consume el backend
.NET ya construido y expone el flujo completo:

```
Tutores → Sesiones → Chat → Memoria → Auditoría
```

El usuario selecciona un tutor, abre o retoma una sesión de estudio, conversa
con el agente (que llama herramientas de memoria autónomamente), y puede inspeccionar
el estado de la memoria y la auditoría de cambios en tiempo real.

## 2. Alcance del MVP — qué SÍ incluir

- Sidebar con tutores y sus sesiones agrupadas
- Chat completo (historial + envío de mensajes) con estado loading/error
- Selector de ContextLoadProfile por sesión
- Gestión básica de tutores y sesiones (crear, renombrar, eliminar con confirmación)
- Drawer derecho con vista de memoria, auditoría y prompt preview del tutor activo
- Conexión con todos los endpoints del backend existente

## 3. Qué NO incluir (fuera de alcance — no agregar sin pedirlo explícitamente)

- Autenticación / login
- Multiusuario
- Editor de SystemPromptContent con markdown enriquecido (campo de texto plano basta)
- Visualización de PreviousValueJson / NewValueJson de MemoryChange (mostrar
  solo los campos de resumen: Operation, Path, TargetId, Reason, CreatedAtUtc)
- Gráficas o dashboards de progreso
- PWA / Service Workers
- i18n / internacionalización

Si una tarea parece requerir algo de esta lista, detente y pregunta en vez de
improvisar una solución "de paso".

## 4. Stack técnico

- Angular (última versión estable disponible al momento de crear el proyecto)
- Standalone Components — sin NgModules
- Angular Signals — para estado compartido entre componentes (tutor activo,
  sesión activa, profile seleccionado); sin NgRx ni BehaviorSubject
- Angular Router — para rutas
- HttpClient — para llamadas al backend, configurado con la URL base via
  environment; sin librerías HTTP adicionales
- Tailwind CSS — para estilos; sin Angular Material ni otra librería de UI
- TypeScript strict mode activado

## 4b. Configuración inicial del proyecto Angular

- Las decisiones concretas de arranque del proyecto se registran en
  `docs/decisiones.md`.
- Antes de crear o instalar dependencias en un entorno nuevo, verificar con
  `node --version`, `npm --version` y `ng version` si Angular CLI ya existe.

## 5. URL base del backend

El backend corre en `http://localhost:5017`. Configurar en:

```
src/environments/environment.ts       → desarrollo
src/environments/environment.prod.ts  → producción (dejar vacío por ahora)
```

Nunca hardcodear la URL base dentro de los servicios — siempre leer de
`environment.apiUrl`. Si el backend no está corriendo, los errores HTTP deben
manejarse con gracia (ver sección 11).

## 6. Estructura de carpetas

```
src/app/
  core/
    models/          → interfaces TypeScript (Tutor, StudySession, Message, etc.)
    services/        → servicios HTTP (TutorService, SessionService, MessageService,
                       MemoryService, AuditService)
    state/           → AppStateService con los Signals globales
  features/
    sidebar/         → componente del sidebar (lista de tutores y sesiones)
    chat/            → componente principal del chat (historial + input)
    drawer/          → componente del panel derecho (memoria, auditoría, preview)
    tutor-form/      → formulario de creación/edición de tutor
    session-form/    → formulario de creación/edición de sesión
  shared/
    components/      → componentes reutilizables (spinner, confirm-dialog, etc.)
  app.component.ts   → layout raíz: sidebar + router-outlet + drawer
  app.routes.ts      → rutas
```

Si hace falta desviarse de esta estructura, anotarlo en `docs/decisiones-frontend.md`
con la razón.

## 7. Modelos TypeScript (fuente de verdad — no improvisar campos)

Mapean directamente a los DTOs de respuesta del backend.

```typescript
export interface Tutor {
  id: number;
  name: string;
  description: string;
  systemPromptContent: string;
  geminiModel: string;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface StudySession {
  id: number;
  tutorId: number;
  name: string;
  goal: string;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface Message {
  id: number;
  sessionId: number;
  role: 'user' | 'assistant' | 'system' | 'tool';
  content: string;
  createdAtUtc: string;
}

export interface MemoryEntry {
  id: number;
  tutorId: number;
  key: string;
  valueJson: string;
  schemaVersion: number;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface MemoryChange {
  id: number;
  memoryEntryId: number;
  memoryEntryKey: string;
  messageId: number | null;
  operation: string;
  path: string;
  targetId: string;
  reason: string;
  createdAtUtc: string;
}

export interface PagedResult<T> {
  page: number;
  pageSize: number;
  totalCount: number;
  items: T[];
}

export type ContextLoadProfile =
  'Minimal' | 'Standard' | 'Evaluation' | 'Project' | 'FullReview';
```

## 8. Estado global con Signals (AppStateService)

Un único servicio `AppStateService` en `core/state/` mantiene el estado compartido
entre Sidebar, Header, Chat y Drawer. Todos los componentes leen de ahí via Signals;
nunca pasan el tutor/sesión activo por `@Input()` entre componentes hermanos.

```typescript
// Señales que debe exponer AppStateService (como mínimo):
activeTutor = signal<Tutor | null>(null);
activeSession = signal<StudySession | null>(null);
activeProfile = signal<ContextLoadProfile>('Standard');
drawerOpen = signal<boolean>(false);
```

Reglas:
- Solo `AppStateService` llama a `.set()` sobre estos signals.
- Los componentes solo llaman métodos del servicio (ej. `appState.selectTutor(tutor)`),
  nunca mutan los signals directamente.
- Cuando cambia el tutor activo, la sesión activa debe resetearse a `null`
  automáticamente — nunca dejar una sesión "activa" de un tutor distinto al
  que está seleccionado.

## 9. Servicios HTTP

Un servicio por dominio, todos en `core/services/`. Cada método devuelve
`Observable<T>` — no `.subscribe()` dentro del servicio, eso es responsabilidad
del componente o del efecto que lo consume.

La fuente de verdad para la lista completa de endpoints, cuerpos esperados y
flujos es `docs/endpoints_y_flujos.md`. Si hay diferencia entre esta tabla
resumida y ese documento, seguir `docs/endpoints_y_flujos.md`.

| Servicio | Endpoints que cubre |
|---|---|
| `TutorService` | GET /api/tutors, GET /api/tutors/{id}, POST /api/tutors, PUT /api/tutors/{id}, DELETE /api/tutors/{id}, GET /api/tutors/{id}/prompt-preview?profile=Standard |
| `SessionService` | GET /api/study-sessions, GET /api/study-sessions/{id}, POST /api/study-sessions, PUT /api/study-sessions/{id}, DELETE /api/study-sessions/{id} |
| `MessageService` | GET /api/sessions/{sessionId}/messages?page=1&pageSize=50, GET /api/messages/{id}, POST /api/messages, POST /api/sessions/{sessionId}/messages |
| `MemoryService` | GET /api/memory-entries, GET /api/memory-entries/{id}, POST /api/memory-entries, PUT /api/memory-entries/{id}, DELETE /api/memory-entries/{id} |
| `AuditService` | GET /api/tutors/{tutorId}/memory-changes?page=1&pageSize=20, GET /api/memory-entries/{memoryEntryId}/memory-changes?page=1&pageSize=20, GET /api/memory-changes, GET /api/memory-changes/{id} |

El método de envío de mensaje (`MessageService.sendMessage`) recibe también el
`ContextLoadProfile` activo y lo incluye en el body del request:

```typescript
sendMessage(sessionId: number, content: string, profile: ContextLoadProfile): Observable<Message>
```

El flujo normal del chat nunca debe cargar historial con `GET /api/messages`
sin filtro. Ese endpoint queda reservado para administración/diagnóstico.
Para el chat activo usar siempre `GET /api/sessions/{sessionId}/messages`
con paginación.

## 10. Roadmap de sprints

1. **Base Angular** — proyecto, environments, routing, servicios HTTP, modelos
   TypeScript, AppStateService con Signals, Tailwind configurado
2. **Layout principal** — estructura visual (sidebar + main + drawer), navegación
   entre rutas, estado de tutor/sesión activo sincronizado entre zonas
3. **Chat funcional** — historial de mensajes, envío con loading/error explícito,
   selector de ContextLoadProfile, respuesta real de Gemini visible
4. **Gestión básica** — crear/editar/eliminar tutor y sesión desde la UI, sin
   Swagger
5. **Drawer derecho** — panel de memoria (MemoryEntry por tutor), auditoría
   (MemoryChange paginado), prompt preview

**Importante:** trabajar un sprint a la vez. El slice mínimo usable es:

```
Sidebar → seleccionar sesión → chat → enviar mensaje → recibir respuesta
```

Confirmar que este slice funciona de punta a punta antes de agregar Drawer, CRUD
y funciones secundarias.

## 11. Manejo de errores HTTP

No dejar que los errores HTTP lleguen silenciosos al usuario. Reglas mínimas:

- **HTTP 429 / 503** (Gemini sobrecargado): mostrar mensaje claro
  ("El tutor está ocupado en este momento, intenta en unos segundos") sin romper
  la UI — el input debe seguir habilitado para reintentar.
- **HTTP 404**: si el tutor o la sesión ya no existen, redirigir al inicio y
  limpiar el estado activo.
- **HTTP 500 / red caída**: mostrar error genérico con opción de reintentar,
  sin pantalla en blanco.
- El componente de chat específicamente debe manejar el caso de "mensaje enviado
  pero sin respuesta" (el backend puede devolver un mensaje de fallback amable
  si Gemini falla) — mostrar ese fallback igual que una respuesta normal, no como
  un error rojo.

## 12. Diseño visual y tokens de color

La UI usa una paleta personalizada con soporte para modo oscuro y modo claro,
alternables por el usuario via toggle. Los tokens se definen como CSS custom
properties en `:root` y se sobreescriben en `[data-theme="light"]`.

**El modo por defecto es oscuro.**

### Tokens — modo oscuro (por defecto)

```css
:root {
  /* Fondos */
  --color-canvas:    #141414;   /* fondo general de la app */
  --color-surface:   #1E1E1C;   /* sidebar, drawer */
  --color-surface-2: #252523;   /* burbujas de chat, cards dentro del drawer */

  /* Acción / marca */
  --color-primary:       #7A9455;   /* texto activo, badges, bordes de énfasis */
  --color-primary-dim:   #4E5B37;   /* botones, avatares, send button */
  --color-primary-light: rgba(122,148,85,0.14); /* fondos sutiles activos */

  /* Secundario */
  --color-secondary: #7A9A9E;   /* iconos, operaciones neutras en auditoría */

  /* Texto */
  --color-text-primary:   #E8E6E0;
  --color-text-secondary: #8A8880;

  /* Bordes */
  --color-border:       rgba(255,255,255,0.07);
  --color-border-strong: rgba(255,255,255,0.11);

  /* Burbuja usuario */
  --color-bubble-user-bg:   #4E5B37;
  --color-bubble-user-text: #E8F0DC;
}
```

### Tokens — modo claro (override)

```css
[data-theme="light"] {
  --color-canvas:    #F5F6F7;
  --color-surface:   #E1DED7;
  --color-surface-2: #D8D5CE;

  --color-primary:       #4E5B37;
  --color-primary-dim:   #3D4829;
  --color-primary-light: rgba(78,91,55,0.12);

  --color-secondary: #5E7174;

  --color-text-primary:   #0F0F0F;
  --color-text-secondary: #575757;

  --color-border:        rgba(0,0,0,0.09);
  --color-border-strong: rgba(0,0,0,0.14);

  --color-bubble-user-bg:   #4E5B37;
  --color-bubble-user-text: #F0F4E8;
}
```

### Qué son los tokens

Los tokens son nombres semánticos reutilizables para los colores del diseño.
Primero se definen como CSS custom properties (`--color-*`) y luego Tailwind
los expone como clases utilitarias. Así los componentes usan clases como
`bg-canvas`, `bg-surface`, `text-text-primary` o `border-border` sin hardcodear
valores hexadecimales.

Esto permite que el modo oscuro y el modo claro cambien los valores reales de
los colores sin modificar los templates de Angular.

### Reglas de uso

- Usar **siempre los tokens**, nunca valores hex hardcodeados en componentes.
- El toggle escribe `data-theme="light"` en `<html>` y persiste la preferencia
  en `localStorage` bajo la key `'synapse-theme'`. En ausencia de preferencia
  guardada, usar modo oscuro.
- `--color-primary` (más claro) es para texto y bordes de énfasis sobre fondos
  oscuros. `--color-primary-dim` (más oscuro) es para superficies sólidas de
  acción (botones, avatares) donde necesita contrastar con texto blanco.
- En modo claro, ambos roles se invierten: `--color-primary` es el verde olivo
  oscuro (`#4E5B37`), que contrasta bien sobre fondos claros.
- No usar `--color-secondary` para texto principal — solo para elementos
  de soporte (iconos, badges de operaciones neutras, bordes secundarios).

### Tailwind — configuración requerida

Extender `tailwind.config.js` para exponer los tokens como clases utilitarias:

```js
theme: {
  extend: {
    colors: {
      canvas:     'var(--color-canvas)',
      surface:    'var(--color-surface)',
      'surface-2':'var(--color-surface-2)',
      primary:    'var(--color-primary)',
      'primary-dim': 'var(--color-primary-dim)',
      'primary-light': 'var(--color-primary-light)',
      secondary:  'var(--color-secondary)',
      'text-primary': 'var(--color-text-primary)',
      'text-secondary': 'var(--color-text-secondary)',
      border: 'var(--color-border)',
      'border-strong': 'var(--color-border-strong)',
      'bubble-user-bg': 'var(--color-bubble-user-bg)',
      'bubble-user-text': 'var(--color-bubble-user-text)',
    }
  }
}
```

Esto permite usar clases como `bg-canvas`, `bg-surface`, `text-text-primary`,
`text-text-secondary`, `border-border`, `bg-primary-dim` o `bg-bubble-user-bg`
directamente en los templates de Angular.

## 12b. Referencia visual

El diseño aprobado está documentado en dos mockups generados antes de iniciar
el desarrollo:

- **Modo oscuro** (paleta por defecto): sidebar `#1E1E1C`, canvas `#141414`,
  primary `#7A9455`, bubbles del asistente en `#252523`, burbuja del usuario
  en `#4E5B37`.
- **Modo claro**: canvas `#F5F6F7`, surface `#E1DED7`, primary `#4E5B37`.

Si OpenCode genera colores distintos a estos en cualquier componente, es una
desviación del diseño aprobado — corregir antes de continuar con el sprint.

## 13. Convenciones de código

- Todos los componentes son Standalone — nunca usar `NgModule` para declararlos.
- Usar `inject()` en vez de constructor injection donde sea posible (patrón moderno
  de Angular 17+).
- `OnPush` change detection en todos los componentes que consuman Signals.
- Nombrado en inglés para código (clases, métodos, variables, archivos).
- Archivos de componente: `nombre.component.ts` + `nombre.component.html` +
  `nombre.component.scss` (o inline styles con Tailwind si son simples).
- No usar `any` — tipar explícitamente todo, incluyendo respuestas HTTP.
- Los `Observable` de los servicios deben manejarse con `async pipe` en templates
  o con `toSignal()` — evitar `.subscribe()` manual excepto cuando sea el único
  camino (y en ese caso, siempre desuscribirse con `takeUntilDestroyed()`).

## 14. Notas de integración con el backend

- El backend ya maneja el provisioning de las 5 `MemoryEntry` al crear un tutor —
  el frontend no necesita crearlas manualmente ni mostrar ningún paso de
  "configuración de memoria".
- La continuidad de sesión ya es responsabilidad del backend — el frontend solo
  necesita mantener el `sessionId` activo y mandar los mensajes al endpoint
  correcto; no necesita rastrear historial localmente.
- Los intercambios de Tool Calling (leer_memoria, guardar_memoria) son internos
  al backend — el frontend nunca los ve ni necesita saber de ellos; solo recibe
  el mensaje final del asistente.
- El endpoint de `prompt-preview` (`GET /api/tutors/{id}/prompt-preview`) es solo
  para el Drawer de diagnóstico, no para el flujo de chat normal.
- El `ContextLoadProfile` por defecto es `Standard` — mostrarlo seleccionado en
  el selector desde el primer render, sin esperar a que el usuario lo cambie.

## 15. Registro de decisiones

Cualquier desviación de este documento (cambio de stack, de estructura de carpetas,
de alcance) debe registrarse en `docs/decisiones.md` con fecha y razón,
no solo aplicarse silenciosamente en el código.
