# Movido → /docs/endpoints.md

Este archivo se mantiene por compatibilidad. **Single source ahora en `/docs/endpoints.md`** (21 rutas auditadas).

> Ver `/docs/README.md` y `backend/docs/AGENTS.md:9` para integración.

## 1. Resumen (redirect)

La API expone CRUD de tutores, sesiones, mensajes y memorias, mas endpoints de diagnostico y auditoria para los sprints recientes. La conversacion con tutores pasa por Gemini, puede ejecutar tools de memoria y conserva el hilo por `StudySession` usando los `Message` persistidos en SQLite.

## 2. Endpoints

### Health

| Metodo | Ruta | Descripcion |
|---|---|---|
| GET | `/health` | Verifica que la API esta viva. |

### Tutors

| Metodo | Ruta | Descripcion |
|---|---|---|
| GET | `/api/tutors` | Lista todos los tutores. |
| GET | `/api/tutors/{id}` | Obtiene un tutor por Id. |
| POST | `/api/tutors` | Crea un tutor y provisiona automaticamente sus 5 `MemoryEntry` estandar en la misma transaccion. |
| PUT | `/api/tutors/{id}` | Actualiza un tutor existente. |
| DELETE | `/api/tutors/{id}` | Elimina un tutor. |
| GET | `/api/tutors/{id}/prompt-preview?profile=Standard` | Devuelve el prompt ensamblado para diagnostico. |
| POST | `/api/tutors/{id}/memory-patch` | Aplica un `MemoryPatch` al tutor. Endpoint temporal de diagnostico. |
| GET | `/api/tutors/{tutorId}/memory-changes?page=1&pageSize=20` | Lista la auditoria historica de todas las memorias del tutor. |

### Study Sessions

| Metodo | Ruta | Descripcion |
|---|---|---|
| GET | `/api/study-sessions` | Lista todas las sesiones. |
| GET | `/api/study-sessions/{id}` | Obtiene una sesion por Id. |
| POST | `/api/study-sessions` | Crea una sesion para un tutor. |
| PUT | `/api/study-sessions/{id}` | Actualiza una sesion. |
| DELETE | `/api/study-sessions/{id}` | Elimina una sesion. |

### Conversation / Messages

| Metodo | Ruta | Descripcion |
|---|---|---|
| GET | `/api/messages` | Lista todos los mensajes. |
| GET | `/api/messages/{id}` | Obtiene un mensaje por Id. |
| POST | `/api/messages` | Crea un mensaje manualmente. |
| GET | `/api/sessions/{sessionId}/messages?page=1&pageSize=50` | Lista mensajes de una sesion especifica, paginados y ordenados del mas reciente al mas antiguo. |
| POST | `/api/sessions/{sessionId}/messages` | Flujo de conversacion con Gemini y tool calling. |

### Memory Entries

| Metodo | Ruta | Descripcion |
|---|---|---|
| GET | `/api/memory-entries` | Lista todas las memorias. |
| GET | `/api/memory-entries/{id}` | Obtiene una memoria por Id. |
| POST | `/api/memory-entries` | Crea una memoria. |
| PUT | `/api/memory-entries/{id}` | Actualiza una memoria. |
| DELETE | `/api/memory-entries/{id}` | Elimina una memoria. |
| GET | `/api/memory-entries/{memoryEntryId}/memory-changes?page=1&pageSize=20` | Lista la auditoria historica de una memoria especifica. |

### Memory Changes

| Metodo | Ruta | Descripcion |
|---|---|---|
| GET | `/api/memory-changes` | Lista todos los cambios de memoria. |
| GET | `/api/memory-changes/{id}` | Obtiene un cambio de memoria por Id. |

## 3. Flujos Principales

### 3.1 CRUD de Tutores

1. El cliente crea o actualiza un tutor con `POST`, `PUT` o `DELETE`.
2. El controller llama al `TutorService`.
3. En `POST /api/tutors`, `TutorService.CreateAsync` abre una transaccion, crea el `Tutor` y provisiona sus 5 `MemoryEntry` estandar.
4. Si falla la creacion de cualquier `MemoryEntry`, se revierte tambien la creacion del `Tutor`.
5. En `PUT` y `DELETE`, el service consulta o modifica `LearningAgentsDbContext`.
6. La API devuelve el `ActionResult` correspondiente.

Memorias vacias provisionadas para cada tutor nuevo:

| Key | ValueJson inicial |
|---|---|
| `memoria_sesion` | `{}` |
| `perfil_estudiante` | `{}` |
| `mapa_dominio` | `{ "temas": [] }` |
| `lagunas_o_errores` | `{ "activas": [], "resueltas": [] }` |
| `historial_actividades` | `{ "proyectos": [] }` |

### 3.2 Prompt Builder

1. El cliente llama a `GET /api/tutors/{id}/prompt-preview?profile=...`.
2. El controller llama a `IPromptBuilder`.
3. El builder carga `PROMPT_GLOBAL.md` desde el output de la API y despues agrega el `SystemPromptContent` del tutor.
4. Segun el `ContextLoadProfile`, consulta solo las memorias necesarias.
5. Cada `ValueJson` se renderiza a Markdown con reglas especificas por clave.
6. Si falta `PROMPT_GLOBAL.md` en runtime, se usa fallback: `Actua como un tutor de programacion util y claro.`
7. Se devuelve el prompt ensamblado como texto plano.

### 3.3 Conversacion con Gemini

1. El cliente llama a `POST /api/sessions/{sessionId}/messages`.
2. El controller llama a `ConversationService`.
3. El service persiste el mensaje del usuario.
4. Se construye el system prompt con `IPromptBuilder`.
5. Se llama a `ILLMProvider` (Gemini).
6. Si Gemini responde con `functionCall`, el service ejecuta las tools disponibles.
7. Las tools pueden leer memoria, listar memoria o guardar memoria.
8. Para `Update` o `Resolve` sobre `mapa_dominio` o `lagunas_o_errores`, `guardar_memoria` requiere que el modelo haya llamado antes a `leer_memoria` para esa key en el mismo turno.
9. Si una tool guarda memoria, `IMemoryPatchEngine` crea el `MemoryChange` correspondiente.
10. El bucle se repite hasta que Gemini responda texto final o se alcance el limite de 5 iteraciones.
11. Solo se persisten el mensaje final del usuario y la respuesta final del assistant; las respuestas intermedias de tools no se guardan como `Message`.
12. Si Gemini devuelve `429` o `503`, `GeminiProvider` reintenta hasta 3 veces con backoff y respeta `Retry-After` cuando viene disponible.
13. Si se agotan los retries por sobrecarga temporal, la API responde con un mensaje amable persistido como respuesta del assistant, no con 500.

Body esperado:

```json
{
  "content": "mensaje del usuario",
  "profile": "Standard"
}
```

`profile` usa `ContextLoadProfile`: `Minimal`, `Standard`, `Evaluation`, `Project`, `FullReview`.

### 3.4 Memory Patch

1. El cliente llama a `POST /api/tutors/{id}/memory-patch`.
2. El controller llama a `IMemoryPatchEngine`.
3. El engine valida la clave, operacion, path, targetId y value.
4. Si el patch es valido, actualiza `MemoryEntry.ValueJson`.
5. En la misma transaccion crea un `MemoryChange` con before/after.
6. Si algo falla, se devuelve `400 Bad Request`.

Ejemplo `Add` para `mapa_dominio`:

```json
{
  "key": "mapa_dominio",
  "operation": "Add",
  "path": "/temas",
  "value": {
    "id": "dialogo-subtexto-voces",
    "nombre": "Dialogo con Subtexto y Voces Distinguibles",
    "nivel": 3,
    "notas": "Demostro evaluacion formal sin ayuda."
  },
  "reason": "Alta de tema tras evaluacion formal"
}
```

Ejemplo `Update` sobre un elemento de `mapa_dominio`:

```json
{
  "key": "mapa_dominio",
  "operation": "Update",
  "targetId": "tema-clean-architecture-dotnet",
  "path": "/temas/nivel",
  "value": 3,
  "reason": "Demostro aplicacion practica sin ayuda en evaluacion formal"
}
```

Para arrays con `targetId`, `targetId` debe ser el campo `id` real del elemento, no su nombre visible. El `path` debe apuntar a un campo especifico, por ejemplo `/temas/nivel` o `/temas/notas`, nunca al nombre del tema.

### 3.5 Auditoria

1. El cliente consulta `GET /api/tutors/{tutorId}/memory-changes` o `GET /api/memory-entries/{memoryEntryId}/memory-changes`.
2. El service busca los cambios historicos.
3. El resultado incluye `MemoryEntryKey`, orden descendente por `CreatedAtUtc`.
4. La respuesta se pagina con `page` y `pageSize`.

### 3.6 Continuidad de StudySession

1. Cada llamada a `POST /api/sessions/{sessionId}/messages` reconstruye el hilo leyendo todos los `Message` de esa `StudySession` desde SQLite.
2. El estado conversacional no depende de memoria RAM del proceso de la API.
3. Tras reiniciar completamente la API, una `StudySession` existente conserva el contexto si sus mensajes estan persistidos.
4. `IPromptBuilder` reconstruye el contexto de fondo del tutor en cada request: prompt global, prompt especifico del tutor y memorias segun profile.

## 4. Endpoints Temporales de Diagnostico

Estos endpoints existen para verificar comportamiento de sprints y pueden retirarse mas adelante:

- `GET /api/tutors/{id}/prompt-preview?profile=Standard`
- `POST /api/tutors/{id}/memory-patch`

## 5. Notas Importantes

- `MemoryChangesController` solo permite `GET` y `GET/{id}`.
- Los registros de auditoria no se crean manualmente, solo desde `IMemoryPatchEngine`.
- El historial de tool calling no se persiste como `Message`; solo se guarda el mensaje final del usuario y la respuesta final del assistant.
- Todo tutor creado desde `POST /api/tutors` debe nacer con sus 5 `MemoryEntry` estandar.
- Tutores antiguos que no tengan memorias deben corregirse con alta puntual de `MemoryEntry`; el flujo automatico aplica para creaciones futuras.
- `POST /api/tutors/{id}/memory-patch` es diagnostico/manual; el flujo normal de memoria autonoma ocurre desde `POST /api/sessions/{sessionId}/messages` mediante tool calling.
