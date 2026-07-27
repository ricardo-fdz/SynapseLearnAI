# AGENTS.md — Learning Agents Platform (MVP Backend)

Este archivo es el contexto persistente del proyecto para agentes de código (OpenCode).
Léelo completo antes de generar o modificar código. Si una instrucción del usuario
contradice este archivo, pregunta antes de proceder.

---

## 1. Qué es este proyecto

Backend de una plataforma de aprendizaje asistido basada en agentes (tutores) con
memoria persistente administrada autónomamente por el propio agente vía Tool Calls.

Problema que resuelve: los chats de IA normales no conservan progreso de aprendizaje
entre sesiones. Esta plataforma sí, mediante memoria estructurada por tutor.

## 2. Alcance del MVP — qué SÍ incluir

- Creación de tutores personalizados (prompt configurable + modelo Gemini)
- Creación y gestión de sesiones de estudio (StudySession)
- Historial completo de mensajes (Message)
- Integración con Gemini API
- Memoria persistente por tutor (MemoryEntry)
- Actualización automática de memoria mediante Tool Calls del propio agente
- Auditoría completa de cambios de memoria (MemoryChange)
- SQLite como almacenamiento local

## 3. Qué NO incluir (fuera de alcance — no agregar sin pedirlo explícitamente)

- Frontend Angular
- Autenticación / autorización
- Soporte multiusuario
- Embeddings / RAG / búsqueda semántica
- Múltiples proveedores LLM simultáneos (la interfaz debe permitirlo a futuro,
  pero solo se implementa GeminiProvider en el MVP)
- Memoria específica por sesión (la memoria vive a nivel Tutor, no StudySession)

Si una tarea parece requerir algo de esta lista, detente y pregunta en vez de
improvisar una solución "de paso".

## 4. Stack técnico

- .NET (Web API) — usar la versión LTS más reciente disponible en el entorno
- Entity Framework Core
- SQLite como proveedor de base de datos
- Swagger/OpenAPI para documentación de la API
- Integración HTTP directa con Gemini API (sin SDK pesado, usar HttpClient)

## 5. Arquitectura

```
Cliente
    │
    ▼
.NET Web API
    │
    ├── Tutor Service
    ├── Session Service
    ├── Prompt Builder
    ├── Memory Service
    ├── Gemini Provider
    └── Memory Patch Engine
    │
    ▼
SQLite
```

Estructura de carpetas sugerida (vertical slice ligero, no Clean Architecture completa
— este es un MVP, evitar sobre-ingeniería):

```
/src
  /LearningAgents.Api          → controllers, Program.cs, DI, Swagger
  /LearningAgents.Domain       → entidades, enums, interfaces de dominio
  /LearningAgents.Infrastructure → DbContext, repositorios, GeminiProvider
  /LearningAgents.Application  → servicios (TutorService, MemoryService, PromptBuilder, MemoryPatchEngine)
/tests
  /LearningAgents.Tests
```

Si en algún sprint hace falta desviarse de esta estructura, anótalo en
`docs/decisiones.md` con la razón.

## 6. Entidades de dominio (fuente de verdad — no improvisar campos)

### Tutor
`Id, Name, Description, SystemPromptContent, GeminiModel, CreatedAtUtc, UpdatedAtUtc`

### StudySession
`Id, TutorId, Name, Goal, CreatedAtUtc, UpdatedAtUtc`

### Message
`Id, SessionId, Role, Content, CreatedAtUtc`
Roles válidos: `user`, `assistant`, `system`, `tool` (validar con enum o constantes,
no strings libres en la lógica de negocio)

### MemoryEntry
`Id, TutorId, Key, ValueJson, SchemaVersion, CreatedAtUtc, UpdatedAtUtc`
Claves estándar (no inventar otras sin justificación):
`memoria_sesion, perfil_estudiante, mapa_dominio, lagunas_o_errores, historial_actividades`

### MemoryChange (auditoría)
`Id, MemoryEntryId, MessageId (nullable), Operation, Path, TargetId, PreviousValueJson, NewValueJson, Reason, CreatedAtUtc`

## 7. Memory Patch Engine

Operaciones soportadas (enum `MemoryPatchOperation`): `Set, Add, Update, Resolve`

Principio clave: las modificaciones de memoria son **parciales**, nunca se sobrescribe
el documento JSON completo de una `MemoryEntry`. Cada patch debe generar su
correspondiente `MemoryChange` para auditoría.

## 8. Prompt Builder — Context Profiles

Enum `ContextLoadProfile`: `Minimal, Standard, Evaluation, Project, FullReview`

| Profile     | Memorias que carga                                          |
|-------------|---------------------------------------------------------------|
| Standard    | perfil_estudiante, memoria_sesion, mapa_dominio, lagunas activas |
| Evaluation  | perfil_estudiante, mapa_dominio, lagunas activas              |
| Project     | perfil_estudiante, mapa_dominio, historial_actividades        |
| FullReview  | todas las memorias                                            |

## 9. Integración LLM

Interfaz fija (no romper el contrato aunque solo haya un proveedor implementado):

```csharp
public interface ILLMProvider
{
    Task<LLMResponse> GenerateAsync(PromptRequest request, CancellationToken cancellationToken);
}
```

Implementación del MVP: `GeminiProvider`. Diseñar para que `OpenAIProvider`,
`ClaudeProvider`, `LocalProvider` se puedan agregar después sin tocar el contrato.

## 10. Herramientas globales (Tool Calls que el LLM puede invocar)

- `leer_memoria(key)`
- `guardar_memoria(patch)`
- `listar_memoria()`

## 11. Roadmap de sprints (ver `docs/roadmap.md` para detalle completo)

1. Base técnica — solución, EF Core, SQLite, entidades, migración, Swagger
2. Dominio — CRUD completo de las 5 entidades + seed
3. Prompt Builder — context profiles, render JSON→Markdown, ensamblado de prompt
4. Gemini — provider, API key, endpoint de conversación, persistencia de mensajes
5. Memory Patch — modelo, validaciones, operaciones Set/Add/Update/Resolve, tests
6. Tool Calling — definición de tools, procesamiento, aplicación automática de patches
7. Auditoría — registro automático de MemoryChange, consulta histórica
8. Vertical Slice MVP — integración completa del flujo Usuario → Tutor → Memoria → Auditoría

**Importante**: trabajar un sprint a la vez. No adelantar trabajo de sprints
posteriores aunque parezca "más eficiente" hacerlo de una vez — el roadmap está
ordenado por dependencias intencionalmente.

## 12. Convenciones de código

- Inyección de dependencias para todos los servicios (Tutor, Session, Memory, Gemini, etc.)
- DTOs separados de entidades de dominio para requests/responses de la API
- Nombrado en inglés para código (clases, métodos, variables); el dominio del
  negocio (claves de memoria, nombres de entidad en el doc original) puede
  quedar en español si ya está establecido así en el diseño
- Async/await en toda operación de I/O (DB, HTTP a Gemini)
- Manejo de errores explícito en llamadas a Gemini (timeouts, rate limits, respuestas malformadas)
- Tests unitarios obligatorios para Memory Patch Engine (es el componente más
  propenso a bugs silenciosos: corrupción de JSON, paths inválidos)

## 13. Registro de decisiones

Cualquier desviación de este documento (cambio de stack, de estructura de
carpetas, de alcance) debe registrarse en `docs/decisiones.md` con fecha y razón,
no solo aplicarse silenciosamente en el código.
