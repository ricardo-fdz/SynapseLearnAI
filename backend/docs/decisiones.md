# Registro de decisiones — Learning Agents Platform

Este archivo registra cualquier decisión que se desvíe de `AGENTS.md` o del
documento de diseño original. Formato: fecha, decisión, razón.

---

## Plantilla de entrada

```
### YYYY-MM-DD — Título corto de la decisión

**Contexto:** ¿Qué problema o duda surgió?

**Decisión:** ¿Qué se decidió hacer?

**Razón:** ¿Por qué se eligió esta opción sobre las alternativas?

**Sprint:** Número de sprint en el que ocurrió
```

---

<!-- Agregar nuevas entradas debajo de esta línea -->

### 2026-08-12 — Decisión de diseño: la meta de sesión (Goal) se mantiene informativa, no imperativa

**Contexto:** Duda de diseño sobre si `StudySession.Goal` debe regir una sesión. Hoy el campo se persiste (DTO `CreateStudySessionRequest`, validado `[Required, StringLength(1000)]`) pero **no se inyecta en ningún prompt**: el grep de `Goal` en `PromptBuilder`/`ConversationService` no arroja uso, y el objetivo que el tutor conoce proviene de `perfil_estudiante.objetivo_declarado` (largo plazo, mutable por el propio agente). Riesgo percibido: volver la sesión rígida si el Goal se usara como mandato fijo e inmutable.

**Decisión:** Se adopta el diseño de **Goal informativo, no imperativo**:
- Si se llega a inyectar en el prompt en el futuro, será como una línea de contexto del tipo "Meta declarada de esta sesión: X. Es una guía, no un límite; priorízala pero no ignores desvíos que favorezcan el objetivo de largo plazo del perfil."
- El Goal permanece **editable** a mitad de sesión (PUT existente: `UpdateStudySessionRequest` permite cambiarlo).
- Queda **subordinado** al `objetivo_declarado` de `perfil_estudiante`: la brújula de largo plazo manda; la meta de sesión es el foco del día.
- Está permitido que el propio tutor marque la meta como cumplida/pivotada en memoria, igual que ya hace con `perfil_estudiante`; no se trata como valor inmutable.

**Razón:** La rigidez no es inherente al campo, sino a cuánto peso se le da en el prompt y a si es mutable. Con un Goal informativo + editable + subordinado al objetivo de largo plazo se gana enfoque por sesión sin sacrificar flexibilidad ni *over-tuning* del agente. No se implementa aún: es decisión de diseño con recomendación, pendiente de ejecución.

**Sprint:** Post-MVP (validación de robustez de memoria).

---

### 2026-07-02 — Soporte multi-provider mediante LLM profiles

**Contexto:** `AGENTS.md` marcaba multiples proveedores LLM como fuera de alcance del MVP, pero Ricardo solicito explicitamente probar Groq y OpenRouter para comparar comportamiento entre modelos/agentes y facilitar que usuarios futuros del repositorio usen la API de pago o gratuita que prefieran.

**Decisión:** Se agrego `LlmProfile` al tutor y un `ILLMProviderRouter` que resuelve perfiles configurados (`gemini`, `groq`, `openrouter`) con fallback ordenado. Gemini queda como perfil estable por defecto (`gemini-default`). Groq y OpenRouter quedan como perfiles experimentales configurables sin exponer API keys.

**Razón:** Permite probar proveedores alternativos sin romper la version estable. Cada request usa un solo provider/modelo activo; solo se intenta otro perfil si el primario falla por cuota/sobrecarga/timeout. Esto evita costo y latencia innecesarios y mantiene tool calling auditable.

**Sprint:** Extension post-MVP solicitada explicitamente por el usuario.

---

### 2026-06-24 — Reversión de Guid a int en todas las entidades

Contexto: Durante el Sprint 1, OpenCode generó todas las entidades de
dominio (Tutor, StudySession, Message, MemoryEntry, MemoryChange) usando
Guid como tipo de Id y en sus foreign keys, sin que se solicitara
explícitamente y sin registrarlo como desviación. El diseño original y
AGENTS.md especifican int autoincremental.

Decisión: Se revirtió el tipo de Id de Guid a int autoincremental en
las 5 entidades y en todas las FKs (TutorId, SessionId, MemoryEntryId,
MessageId). Se regeneró la migración InitialCreate desde cero.

Razón: Este es un MVP local, de un solo usuario, sin sincronización
distribuida ni necesidad de IDs no adivinables expuestos públicamente. int
es más simple de leer durante debugging y no aporta complejidad innecesaria
para este alcance. Se prefirió revertir en el Sprint 1, antes de que el
Sprint 2 (CRUD) y sprints posteriores construyeran sobre Guid.

Sprint: 1 (corrección aplicada antes de iniciar Sprint 2)

Nota para sprints futuros: Vigilar que OpenCode no reintroduzca Guid
por inercia en código nuevo (por ejemplo, al generar DTOs o servicios en el
Sprint 2). Si vuelve a aparecer, recordarle este archivo.

---
 
### 2026-06-24 — Fix de NU1903 (CVE-2025-6965 en SQLitePCLRaw.lib.e_sqlite3)
 
**Contexto:** El build mostraba la advertencia NU1903, originada por una
versión vulnerable de SQLite (< 3.50.2) traída transitivamente por
`Microsoft.EntityFrameworkCore.Sqlite 10.0.9`. La vulnerabilidad (CVE-2025-6965)
permite corrupción de memoria con queries que excedan el número de columnas
disponibles en términos agregados.
 
**Decisión:** Se agregó una referencia directa a
`SQLitePCLRaw.bundle_e_sqlite3 3.0.3` en
`src/LearningAgents.Infrastructure/LearningAgents.Infrastructure.csproj`, lo
cual fuerza la resolución a `SourceGear.sqlite3 3.50.4.5`.
 
**Razón:** Es la forma estándar de sobreescribir una dependencia transitiva
vulnerable sin esperar a que el paquete padre (EF Core Sqlite) actualice su
propia referencia. Riesgo práctico era bajo dado que la API no expone SQL
crudo y corre local sin exposición pública, pero no había razón para
mantener deuda técnica evitable.
 
**Sprint:** 2
 
**Nota:** Si en el futuro se actualiza `Microsoft.EntityFrameworkCore.Sqlite`
a una versión que ya incluya una versión segura de SQLite por defecto, esta
referencia directa se puede remover (verificar antes de quitarla que no rompa
el build).

---
 
### 2026-06-24 — Refactor: extracción de Service layer (Application)
 
**Contexto:** Los controllers en `LearningAgents.Api` inyectaban
`LearningAgentsDbContext` directamente, violando la separación de capas
definida en `AGENTS.md` (Api solo debe depender de Domain y Application,
nunca de Infrastructure).
 
**Decisión:** Se introdujo una interfaz por entidad en
`LearningAgents.Application/Interfaces` (`ITutorService`,
`IStudySessionService`, `IMessageService`, `IMemoryEntryService`,
`IMemoryChangeService`) con su implementación correspondiente en
`LearningAgents.Application/Services`. Los controllers ahora dependen
únicamente de estas interfaces. El registro de DI se encapsuló en
`LearningAgents.Application/DependencyInjection.cs`, expuesto como el método
de extensión `AddApplicationServices()`, invocado desde `Program.cs`.
 
**Razón:** Cada capa registra sus propias dependencias — `Program.cs` no
necesita conocer qué servicios concretos existen dentro de `Application`,
solo invoca `AddApplicationServices()`. Esto mantiene el punto de entrada
limpio y centraliza el registro junto a las clases que registra.
 
**Patrón a seguir en sprints futuros:** Cualquier nuevo servicio de
Application (ej. `IMemoryPatchEngine` en Sprint 5, `IPromptBuilder` en
Sprint 3) debe registrarse dentro de `AddApplicationServices()`, no
directamente en `Program.cs`.
 
**Verificado:** `LearningAgents.Api.csproj` ya no referencia
`LearningAgents.Infrastructure` directamente. Build limpio y smoke test de
los 5 endpoints sin cambio de comportamiento.
 
**Sprint:** 2 (corrección aplicada antes de iniciar Sprint 3)

---

### 2026-06-24 — Prompt Builder con esquemas de memoria ausentes

**Contexto:** Sprint 3 pide implementar renderers JSON→Markdown específicos
según `docs/esquemas_memoria.md`, pero en el repositorio el archivo disponible
es `docs/Esquemas memoria.md` y está vacío. Tampoco existe `docs/roadmap.md`;
la tabla de perfiles disponible está en `docs/AGENTS.md`.

**Decisión:** Se implementaron renderers específicos por clave estándar usando
los nombres de memoria definidos en `AGENTS.md` y formato Markdown tolerante a
campos faltantes. `Minimal` no carga memoria porque la tabla solo especifica
Standard, Evaluation, Project y FullReview.

**Razón:** Permite completar el Sprint 3 sin introducir nuevas entidades ni
adelantar sprints posteriores. Cuando el documento de esquemas se complete, los
renderers pueden ajustarse sin cambiar el contrato público de `IPromptBuilder`.

**Sprint:** 3
### 2026-06-25 — Bug: seed de MemoryEntry con ValueJson mal formado
 
**Contexto:** Al implementar el Sprint 3 (Prompt Builder), el endpoint de
preview (`GET /api/tutors/{id}/prompt-preview`) mostraba todas las memorias
como "no registradas" para el tutor del seed (Id=1), a pesar de que el
smoke test del Sprint 2 confirmaba `memoryEntries: 5`. El TutorId de las 5
filas era correcto — el problema real era que `ValueJson` se sembró con
placeholders incorrectos: `{}` para claves que requieren un array contenedor
(`mapa_dominio`, `historial_actividades`) y `[]` (array suelto) para
`lagunas_o_errores`, que según `docs/esquemas_memoria.md` debe ser un objeto
`{ "activas": [], "resueltas": [] }`.
 
**Decisión:** Se corrigió el seed para que cada `ValueJson` tenga la
estructura correcta pero vacía (representando un tutor recién creado sin
historial), no datos de ejemplo con contenido:
- `memoria_sesion`: `{}`
- `perfil_estudiante`: `{}`
- `mapa_dominio`: `{ "temas": [] }`
- `lagunas_o_errores`: `{ "activas": [], "resueltas": [] }`
- `historial_actividades`: `{ "proyectos": [] }`
Se verificó que el Prompt Builder maneja este caso de "estructura vacía"
sin lanzar excepciones, tanto en el profile `Standard` como en `FullReview`.
 
**Razón:** El smoke test original solo verificaba conteo de filas, no la
forma del contenido — por eso el bug no se detectó hasta que hubo un
consumidor real (el Prompt Builder) leyendo ese JSON. La estructura vacía
correcta (en vez de datos de ejemplo) se eligió porque representa el caso
real que ocurrirá cada vez que se cree un tutor nuevo vía la API.
 
**Sprint:** 3
 
**Nota para sprints futuros:** Si se vuelve a tocar el seed (por ejemplo en
Sprint 5 al probar el Memory Patch Engine), verificar contra
`docs/esquemas_memoria.md` que la forma de cada `ValueJson` sea exactamente
la esperada — un `{}` o `[]` genérico no es intercambiable entre claves.

### 2026-06-25 — Aclaración: propósito de Tutor 1 vs Tutor 3
 
**Contexto:** Durante las pruebas del Sprint 4 (Gemini) surgió una diferencia
notable de comportamiento entre dos tutores existentes en la base de datos
local de desarrollo, lo cual en un primer momento se interpretó como un
posible bug.
 
**Aclaración (no es un bug):**
- **Tutor 1 ("Programming Tutor")**: viene del seed automático (`HasData`)
  del Sprint 2. Tiene un `SystemPromptContent` placeholder genérico
  ("Actua como un tutor de programacion socratico y enfocado en aprendizaje
  real.", 77 caracteres). Su propósito es servir como dato de prueba
  reproducible — existe automáticamente cada vez que se recrea la base de
  datos desde cero, sin depender de contenido externo.
- **Tutor 3**: creado manualmente durante el Sprint 3 para probar el Prompt
  Builder con un caso más rico. Tiene el `SystemPromptContent` completo y
  real de "Profesor Titular Socrático" (el documento de prompt específico
  de programación). Este es el tutor de uso diario real de Ricardo — no es
  un dato de seed, y no se regenera automáticamente si se recrea la base de
  datos.
**Decisión:** Se mantiene el seed del Tutor 1 con el prompt genérico
(decisión explícita, no pendiente). No se sincroniza el contenido del Tutor
3 hacia el seed.
 
**Razón:** Mantener el seed simple y desacoplado del contenido real de uso
evita que una migración de base de datos dependa de mantener sincronizado
un prompt largo que puede cambiar con frecuencia según el uso real.
 
**Sprint:** 4
 
**Nota:** Si la base de datos de desarrollo se recrea desde cero en el
futuro, el Tutor 3 (y su prompt real) se perderá y deberá recrearse
manualmente — no está protegido por ninguna migración o seed.

### 2026-06-25 — Aclaración: propósito de Tutor 1 vs Tutor 3
 
**Contexto:** Durante las pruebas del Sprint 4 (Gemini) surgió una diferencia
notable de comportamiento entre dos tutores existentes en la base de datos
local de desarrollo, lo cual en un primer momento se interpretó como un
posible bug.
 
**Aclaración (no es un bug):**
- **Tutor 1 ("Programming Tutor")**: viene del seed automático (`HasData`)
  del Sprint 2. Tiene un `SystemPromptContent` placeholder genérico
  ("Actua como un tutor de programacion socratico y enfocado en aprendizaje
  real.", 77 caracteres). Su propósito es servir como dato de prueba
  reproducible — existe automáticamente cada vez que se recrea la base de
  datos desde cero, sin depender de contenido externo.
- **Tutor 3**: creado manualmente durante el Sprint 3 para probar el Prompt
  Builder con un caso más rico. Tiene el `SystemPromptContent` completo y
  real de "Profesor Titular Socrático" (el documento de prompt específico
  de programación). Este es el tutor de uso diario real de Ricardo — no es
  un dato de seed, y no se regenera automáticamente si se recrea la base de
  datos.
**Decisión:** Se mantiene el seed del Tutor 1 con el prompt genérico
(decisión explícita, no pendiente). No se sincroniza el contenido del Tutor
3 hacia el seed.
 
**Razón:** Mantener el seed simple y desacoplado del contenido real de uso
evita que una migración de base de datos dependa de mantener sincronizado
un prompt largo que puede cambiar con frecuencia según el uso real.
 
**Sprint:** 4
 
**Nota:** Si la base de datos de desarrollo se recrea desde cero en el
futuro, el Tutor 3 (y su prompt real) se perderá y deberá recrearse
manualmente — no está protegido por ninguna migración o seed.
 
---
 
### 2026-06-25 — Sprint 5 (Memory Patch Engine) cerrado y verificado
 
**Contexto:** Implementación de IMemoryPatchEngine con las 4 operaciones
(Set, Add, Update, Resolve) según los esquemas de docs/esquemas_memoria.md,
con validación estricta (falla con InvalidMemoryPatchException, nunca
aplica patches inválidos silenciosamente — decisión tomada al inicio del
sprint).
 
**Verificación realizada:**
- 13 tests unitarios pasando: 8 de operaciones válidas (una por cada
  combinación clave/operación soportada) + 5 de validación de errores
  (operación incompatible, TargetId faltante, path desconocido, forma de
  Value inválida, key desconocida).
- 3 de los 8 tests de operación válida (Set en memoria_sesion, Update en
  mapa_dominio, Resolve en lagunas_o_errores) tienen aserciones explícitas
  sobre el contenido de MemoryChange (PreviousValueJson, NewValueJson,
  Reason, Operation, MessageId) — no solo sobre el ValueJson final de
  MemoryEntry.
- Prueba manual end-to-end contra el endpoint POST /api/tutors/{id}/memory-patch
  con un tutor real, confirmando los 4 tipos de operación con su
  MemoryChange correctamente persistido (Ids consecutivos 11-15, incluyendo
  el Add previo necesario para poblar el TargetId que luego usó el Resolve).
**Nota técnica relevante:** el fixture de tests requirió crear una
StudySession y un Message reales (no un MessageId inventado), porque
MemoryChange.MessageId tiene FK real contra Messages y SQLite rechaza
valores que no existen.
 
**Sprint:** 5
---
 
### 2026-06-25 — Sprint 6 (Tool Calling) cerrado y verificado
 
**Contexto:** Implementación del bucle de tool calling sobre GeminiProvider,
exponiendo las 3 tools globales (leer_memoria, guardar_memoria,
listar_memoria) definidas en el documento de diseño original, conectadas a
IMemoryPatchEngine (Sprint 5) vía un nuevo MemoryToolHandler.
 
**Decisiones de diseño tomadas en este sprint:**
- El bucle de tool calling soporta múltiples iteraciones en un mismo turno
  (hasta 5 máximo), no solo una tool call por turno — necesario para que el
  modelo pueda leer antes de decidir si guardar, sin gastar una ida y vuelta
  completa con el usuario.
- Si guardar_memoria falla (InvalidMemoryPatchException), el error se
  captura y se devuelve como contenido del function response al LLM, NO se
  propaga como excepción HTTP — permite que el modelo se corrija y reintente
  dentro del mismo turno.
- Los intercambios intermedios de tool calling (function call / function
  response) NO se persisten como Message — solo el mensaje final del
  usuario y la respuesta final del assistant. Las tool calls se loguean a
  consola para debugging, sin tabla nueva.
**Verificación realizada:**
- Build limpio, 13/13 tests del Sprint 5 siguen pasando (no se rompió nada
  existente).
- Prueba end-to-end real con Gemini y el tutor 3: el modelo ejecutó
  correctamente una secuencia de 3 tool calls (listar_memoria → leer_memoria
  → guardar_memoria) en un solo turno, usando un targetId real obtenido de
  la lectura (no inventado).
- Se confirmó el contenido completo del MemoryChange resultante
  (Id=16): PreviousValueJson y NewValueJson muestran que solo el campo
  `notas` cambió, preservando nivel, nombre, id y ultima_evaluacion sin
  alteración — confirma que MemoryToolHandler no corrompe datos al pasar el
  patch desde la tool call hacia el Patch Engine.
- Se probó también el caso de un patch inválido enviado por el modelo:
  confirmado que el error vuelve como tool result y el modelo puede
  reaccionar sin que la conversación se rompa.
**Sprint:** 6
### 2026-06-25 — Auditoría de docs/endpoints_y_flujos.md contra código real
 
**Contexto:** Se generó un documento de referencia (`docs/endpoints_y_flujos.md`)
listando todos los endpoints de la API y sus flujos principales. Antes de
adoptarlo como fuente de verdad, se auditó cada ruta documentada contra el
código real (controllers + Program.cs), ya que la duda inicial sobre
`GET /health` (¿existe o fue inventado?) reveló la necesidad de verificar
en vez de confiar a ciegas en documentación generada.
 
**Método de auditoría:** se generó un inventario real combinando (a) rutas
de Minimal API definidas directamente en `Program.cs` (ej. `GET /health`,
que no vive en un controller) y (b) rutas de los 9 controllers reales vía
sus atributos `[HttpGet]/[HttpPost]/etc.`. Ese inventario se comparó
sección por sección contra el documento.
 
**Resultado:** Cero discrepancias. Las 21 rutas documentadas coinciden
exactamente con las rutas reales del código, incluyendo `GET /health` (que
sí existe, solo que vive en `Program.cs` y no en un controller — lo cual
explicaba la duda inicial sin ser un error real).
 
**Razón para registrar esto:** deja precedente del método a seguir la
próxima vez que se actualice este documento o se agreguen endpoints nuevos
— la auditoría debe cubrir tanto controllers como cualquier ruta Minimal
API en Program.cs, no solo uno de los dos.
 
**Sprint:** post-MVP (mantenimiento de documentación)

---
 
### 2026-06-25 — Guard de orden leer-antes-de-escribir + anti-falso-éxito + reintentos de Gemini
 
**Contexto:** La verificación conversacional de PROMPT_GLOBAL.md (entrada
anterior) reveló dos problemas adicionales al llevar una conversación
completa hasta evaluación formal aprobada: (1) el modelo seguía sin llamar
leer_memoria de forma consistente antes de Update/Resolve, fallando con el
mismo error de targetId ya visto antes; (2) más grave, el tutor afirmó
textualmente al usuario "tu nivel se actualiza a 3" cuando el patch
correspondiente había fallado dos veces y nunca se reintentó — un caso de
éxito falso reportado al usuario.
 
**Decisión:** Ambas correcciones se resolvieron en código, no agregando
texto a PROMPT_GLOBAL.md, para no incrementar el consumo de tokens en cada
request (decisión explícita de Ricardo).
 
1. **Guard de orden forzado**: ConversationService mantiene un registro de
   qué keys de memoria fueron leídas vía leer_memoria DENTRO DEL TURNO
   ACTUAL (el bucle de hasta 5 iteraciones de un mismo mensaje). Si
   guardar_memoria intenta Update/Resolve sobre mapa_dominio o
   lagunas_o_errores sin lectura previa en el mismo turno, se rechaza como
   tool result con mensaje accionable, no como excepción. No aplica a
   memoria_sesion, perfil_estudiante ni historial_actividades (no lo
   necesitan).
2. **Anti-falso-éxito**: si la última tool call de guardar_memoria en el
   turno falló y no hubo una corrección exitosa posterior, se inyecta una
   nota interna transitoria en el contexto de la llamada final a Gemini,
   indicando que no debe afirmar que el progreso se guardó. La nota deja
   de enviarse en cuanto hay un guardar_memoria exitoso.
3. **Reintentos con backoff en GeminiProvider**: ante HTTP 429/503,
   reintenta hasta 3 veces con backoff exponencial (1s, 2s, 4s),
   respetando el header Retry-After si está presente. Si los 3 intentos
   fallan, propaga un error claro (no 502/500 genérico) que
   ConversationService traduce a una respuesta amable al usuario.
**Verificación:**
- 16/16 tests pasando (15 anteriores + 1 nuevo de reintentos, simulando
  429 con Retry-After seguido de 200 exitoso).
- Prueba conversacional real confirmó que el guard de orden SÍ rechaza
  correctamente un intento prematuro de Update sin lectura previa, y que
  el modelo corrigió llamando leer_memoria en la siguiente iteración. No
  se pudo confirmar el patch exitoso final en esta sesión por agotamiento
  de cuota de Gemini (HTTP 429) durante la prueba — pendiente de
  confirmación en una sesión futura con cuota disponible.
**Pendiente para una sesión futura:** repetir la prueba completa (o
continuar la StudySession 10 si el estado de Gemini lo permite) para
confirmar que, con el guard activo, el ciclo completo de evaluación
aprobada → leer_memoria → guardar_memoria con id correcto → MemoryChange
creado, ocurre sin intervención manual.
 
**Nota sobre exploración futura (NO decidida, solo registrada como
intención):** Ricardo expresó interés en evaluar soporte multi-proveedor
(ej. Groq además de Gemini) como fallback ante límites de cuota, pero
identificó correctamente dos riesgos a resolver antes de implementarlo:
(1) distintos modelos pueden tener umbrales de rigor pedagógico distintos
al seguir PROMPT_GLOBAL.md, rompiendo la consistencia de la experiencia de
tutoría; (2) no todos los proveedores/modelos soportan tool calling de
forma equivalente, con riesgo de fallar silenciosamente en la persistencia
de memoria. Se sugirió, para cuando se retome el tema, diseñar primero un
conjunto de casos de prueba de comportamiento (similar a los corridos hoy
de forma manual) para validar cualquier proveedor nuevo antes de
confiarle tutoría real.
 
**Sprint:** post-MVP

---

### 2026-07-06 — Perfil inicial transaccional al crear Tutor

**Contexto:** El frontend estaba creando un tutor y luego disparando varias
llamadas consecutivas a `POST /api/tutors/{id}/memory-patch` para inicializar
`perfil_estudiante` campo por campo (lenguaje principal, objetivo declarado,
ritmo de sesion, nivel de autonomia, reaccion ante errores, tono, idioma,
podian leer el mismo `PreviousValueJson` y terminar sobrescribiendo cambios de
otras llamadas (lost update). El sintoma visible era que el tutor no parecia
tomar en cuenta el ritmo de sesion configurado desde el formulario.

**Decision:** `POST /api/tutors` acepta ahora un bloque opcional
`initialStudentProfile` y `TutorService.CreateAsync` inicializa
`perfil_estudiante` con ese JSON dentro de la misma transaccion que crea el
`Tutor` y sus 5 `MemoryEntry` estandar. `/memory-patch` se mantiene para
actualizaciones posteriores de memoria, pero deja de ser el mecanismo recomendado
para la configuracion inicial del perfil.

**Razon:** El perfil inicial es parte de la creacion del tutor, no un conjunto de
mutaciones autonomas posteriores. Guardarlo transaccionalmente evita lost
updates, reduce ruido de auditoria, simplifica el frontend y garantiza que el
primer `prompt-preview` y la primera conversacion ya incluyan preferencias como
`ritmo_sesion`, `nivel_autonomia`, `reaccion_ante_errores` y `tono_tutor`.

**Verificacion:** Se agrego test para crear tutor sin perfil inicial
(`perfil_estudiante = {}`) y test para crear tutor con perfil inicial completo.
Tambien se valido via HTTP real que el primer `prompt-preview` de un tutor nuevo
incluye lenguaje principal, objetivo declarado, estilo de aprendizaje y
preferencias de comunicacion sin llamadas posteriores a `memory-patch`. Tests:
37/37 pasando.

**Sprint:** post-MVP

### 2026-06-25 — Cierre del ciclo completo: guardar_memoria exitoso tras evaluación
 
**Contexto:** Continuación directa de la entrada anterior. Tras el guard de
orden y la nota anti-falso-éxito, quedaban dos problemas adicionales
descubiertos en pruebas sucesivas dentro de la misma StudySession 10: (1)
el modelo confundía el campo `path` con el `targetId`, usando el nombre
visible del tema como path en vez de apuntar a un campo específico; (2)
intentó actualizar dos campos (`nivel` y `notas`) en una sola llamada, no
soportado por diseño.
 
**Cambio de estrategia:** tras 3 rondas sucesivas de "mejorar mensaje de
error → nuevo tipo de fallo relacionado", se decidió cambiar el enfoque de
corrección reactiva (mensajes de error) a prevención proactiva: se
agregaron EJEMPLOS JSON completos y válidos directamente en la descripción
de la tool guardar_memoria (MemoryToolDeclarations) para mapa_dominio
(Update) y lagunas_o_errores (Resolve), distinguiendo explícitamente
targetId (campo 'id' real) de path (campo específico a modificar). Los
mensajes de error reactivos de rondas anteriores se mantuvieron como red
de seguridad, pero ya no como primera línea de defensa.
 
**Resultado:** patch exitoso al primer intento de guardar_memoria, sin
ningún rechazo del guard. Secuencia: leer_memoria(mapa_dominio) →
guardar_memoria(Update, targetId="tema-angular-signals",
path="/temas/nivel", value=3) → éxito. MemoryChange.Id=18 creado
correctamente. Angular Signals (Computed vs Effect) subió de nivel 1 a
nivel 3, justificado por la evidencia acumulada de toda la conversación
(explicación conceptual + código funcional + caso límite + evaluación
formal aprobada), consistente con el criterio de incremento de nivel
agregado hoy a PROMPT_GLOBAL.md.
 
**Lección para futuras tools:** un ejemplo JSON completo y positivo en la
descripción de una tool previene errores de formato de forma más efectiva
que acumular reglas negativas en mensajes de error tras cada fallo
descubierto. Considerar este enfoque desde el diseño inicial de cualquier
tool nueva que se agregue al sistema, en vez de esperar a que las pruebas
revelen el patrón de confusión primero.
 
**Verificación final de la sesión completa de hoy:** ciclo completo
Usuario → Tutor → Memoria → Auditoría confirmado funcionando de punta a
punta con el modelo real (Gemini), incluyendo el protocolo pedagógico
completo de PROMPT_GLOBAL.md (diagnóstico, enseñanza por capas, práctica
activa, output forzado, pausa de exploración, evaluación adversarial) y
el ciclo de memoria autónoma (lectura, decisión de no escribir prematura,
escritura exitosa tras evidencia suficiente).
 
**Sprint:** post-MVP

### 2026-06-25 — Confirmado: continuidad de sesión específica tras reinicio completo del proceso
 
**Contexto:** Surgió la duda de si retomar una StudySession específica que
quedó a la mitad de una conversación (en este caso, en plena evaluación
formal sin pistas) mantiene el contexto correctamente si el proceso de la
API se reinicia por completo entre medio — un caso real de uso (cerrar la
app y volver otro día a la misma sesión).
 
**Aclaración de diseño relevante:** existen dos mecanismos de contexto
distintos en el sistema, y es importante no confundirlos: (1) IPromptBuilder
ensambla PROMPT_GLOBAL.md + SystemPromptContent + memorias a NIVEL TUTOR,
reconstruido en cada llamada — esto es intencional y correcto, es el
"contexto de fondo" persistente entre sesiones. (2) El historial de
Message vive a NIVEL STUDYSESSION, persistido en la base de datos y
reconstruido en cada llamada a POST /api/sessions/{id}/messages — esto es
lo que da continuidad al HILO conversacional de una sesión específica.
Ambos son independientes del proceso en ejecución, ya que se leen de
SQLite, no de memoria en RAM del proceso.
 
**Verificación realizada:** se mató el proceso de la API por completo, se
confirmó que /health dejó de responder, se volvió a levantar desde cero,
se confirmó /health de nuevo. Se verificó que StudySession 12 (que había
quedado con una pregunta de evaluación formal pendiente sobre Angular
@defer) conservaba sus 4 mensajes intactos en la base de datos. Se envió
una respuesta real a esa pregunta pendiente, sin recordarle nada al tutor
sobre el contexto.
 
**Resultado:** el tutor reconoció correctamente que la respuesta
correspondía a su pregunta de evaluación pendiente, sin tratarlo como un
mensaje desconectado, y continuó la evaluación adversarial con precisión
(incluso señalando un matiz real sobre la elección de trigger de @defer).
Confirma que la continuidad de sesión es robusta ante reinicios del
proceso, ya que toda la persistencia relevante vive en SQLite, no en
estado de memoria del proceso en ejecución.
 
**Sprint:** post-MVP
 
### 2026-06-25 — Provisioning automático de MemoryEntry al crear un Tutor

**Contexto:** Al probar un tutor nuevo creado manualmente (tutor 5,
Escritura creativa) en una conversación real, se descubrió que solo el
tutor del seed (Id=1) recibe sus 5 MemoryEntry automáticamente (vía
HasData en la migración) — cualquier tutor creado después vía
POST /api/tutors nace sin ninguna MemoryEntry. El sistema manejó esto sin
romperse (leer_memoria devolvió un error claro como tool result, no un
500, y el tutor reaccionó correctamente haciendo preguntas de diagnóstico
inicial, como dicta PROMPT_GLOBAL.md para perfil vacío) — pero se decidió
no depender de ese comportamiento orgánico como solución permanente.

**Decisión:** TutorService.CreateAsync ahora provisiona automáticamente
las 5 MemoryEntry estándar (con la estructura vacía correcta, centralizada
en un nuevo MemoryEntryDefaults compartido también por el seed) en la
MISMA transacción que crea el Tutor — si falla la creación de alguna
MemoryEntry, se revierte también la creación del Tutor.

**Razón:** Garantiza que TODO tutor, sin importar cómo se creó, tenga
siempre sus 5 MemoryEntry desde el primer momento — invariante más simple
de razonar que "puede o no tenerlas según el método de creación", y evita
depender de que el modelo decida correctamente hacer un Add la primera vez
que algo falle.

**Corrección de dato existente:** se crearon manualmente las 5 MemoryEntry
para el tutor 5 (que ya existía antes de este fix), confirmando la
estructura correcta en cada una.

**Verificación:** 17/17 tests pasando (incluye nuevo test
CreateAsync_ProvisionsStandardMemoryEntries). Confirmado con un tutor
nuevo real (Id=6) que sus 5 MemoryEntry existen inmediatamente tras la
creación, sin necesitar ninguna conversación. Repetida la conversación de
prueba con el tutor 5: ya no aparece el error de memoria inexistente: el
tutor ahora carga perfil_estudiante={} directamente desde el prompt
inicial (vía IPromptBuilder) sin necesitar llamar leer_memoria para
detectar su ausencia.

**Nota relacionada con decisión previa:** el tutor 1 (seed) y el tutor 3
(prompt real de programación, creado manualmente antes de este fix) ya
tenían sus MemoryEntry por otras vías (seed automático y corrección manual
respectiva) — este fix solo cierra el hueco para tutores creados
posteriormente sin corrección manual explícita.

**Sprint:** post-MVP
---
 
### 2026-06-25 — Provisioning automático de MemoryEntry al crear un Tutor
 
**Contexto:** Al probar un tutor nuevo creado manualmente (tutor 5,
Escritura creativa) en una conversación real, se descubrió que solo el
tutor del seed (Id=1) recibe sus 5 MemoryEntry automáticamente (vía
HasData en la migración) — cualquier tutor creado después vía
POST /api/tutors nace sin ninguna MemoryEntry. El sistema manejó esto sin
romperse (leer_memoria devolvió un error claro como tool result, no un
500, y el tutor reaccionó correctamente haciendo preguntas de diagnóstico
inicial, como dicta PROMPT_GLOBAL.md para perfil vacío) — pero se decidió
no depender de ese comportamiento orgánico como solución permanente.
 
**Decisión:** TutorService.CreateAsync ahora provisiona automáticamente
las 5 MemoryEntry estándar (con la estructura vacía correcta, centralizada
en un nuevo MemoryEntryDefaults compartido también por el seed) en la
MISMA transacción que crea el Tutor — si falla la creación de alguna
MemoryEntry, se revierte también la creación del Tutor.
 
**Razón:** Garantiza que TODO tutor, sin importar cómo se creó, tenga
siempre sus 5 MemoryEntry desde el primer momento — invariante más simple
de razonar que "puede o no tenerlas según el método de creación", y evita
depender de que el modelo decida correctamente hacer un Add la primera vez
que algo falle.
 
**Corrección de dato existente:** se crearon manualmente las 5 MemoryEntry
para el tutor 5 (que ya existía antes de este fix), confirmando la
estructura correcta en cada una.
 
**Verificación:** 17/17 tests pasando (incluye nuevo test
CreateAsync_ProvisionsStandardMemoryEntries). Confirmado con un tutor
nuevo real (Id=6) que sus 5 MemoryEntry existen inmediatamente tras la
creación, sin necesitar ninguna conversación. Repetida la conversación de
prueba con el tutor 5: ya no aparece el error de memoria inexistente: el
tutor ahora carga perfil_estudiante={} directamente desde el prompt
inicial (vía IPromptBuilder) sin necesitar llamar leer_memoria para
detectar su ausencia.
 
**Nota relacionada con decisión previa:** el tutor 1 (seed) y el tutor 3
(prompt real de programación, creado manualmente antes de este fix) ya
tenían sus MemoryEntry por otras vías (seed automático y corrección manual
respectiva) — este fix solo cierra el hueco para tutores creados
posteriormente sin corrección manual explícita.
 
**Sprint:** post-MVP
 
---

### 2026-07-03 — Implementación del router multi-proveedor LLM

**Contexto:** Las pruebas empíricas con múltiples proveedores LLM
(Gemini, Groq, OpenRouter) demostraron que Gemini es el más consistente
pedagógicamente, pero puede alcanzar límites de cuota (429/503). Se
implementó un router con fallback automático entre proveedores para
mejorar la resiliencia sin exponer complejidad al usuario.

**Clases nuevas en LearningAgents.Infrastructure/LLM:**
- `OpenAICompatibleProvider`: clase base abstracta para proveedores con
  API compatible con OpenAI (Groq, OpenRouter). Hereda de ILLMProvider.
  Sobreescribir solo ProviderName, Endpoint y ApiKey para agregar un
  proveedor nuevo.
- `GroqProvider`: implementación para Groq, hereda de
  OpenAICompatibleProvider. Configurado con GroqOptions (sección "Groq"
  en user-secrets).
- `OpenRouterProvider`: implementación para OpenRouter, misma base.
- `LLMProviderRouter`: implementa ILLMProviderRouter. Lee la cadena de
  perfiles desde LlmProfilesOptions y los intenta en orden hasta obtener
  una respuesta exitosa.
- `LlmProfilesOptions`: configuración de perfiles en appsettings.json,
  sección "LlmProfiles". Cada perfil define provider, model y
  fallbackProfiles (array).

**Cadena de fallback activa en producción:**
gemini-default → groq-oss-20b → groq-qwen-32b

**Criterio de fallback (ShouldTryFallback):** códigos 402, 408, 429,
503, o mensajes que contengan "timed out" o "did not contain generated
text". HTTP 400 fue removido deliberadamente (indica request malformado,
no error transitorio — hacer fallback con el mismo payload defectuoso
ocultaría bugs reales de serialización).

**Resultados empíricos de las pruebas (2026-07-03):**
- gemini-default: más consistente pedagógicamente, mejor seguimiento de
  PROMPT_GLOBAL.md. Default recomendado.
- groq-oss-20b: viable como alternativa experimental. Tool calling
  correcto tras reforzar MemoryToolDeclarations. Sensible a límites TPM.
  Confirmado: guardó correctamente MemoryChange.Id=40 (mapa_dominio, Add,
  tema Clean Code SRP, nivel 3) en tutor 19, sesión 31.
- groq-qwen-32b: tool calling correcto (MemoryChange.Id=41), pero cayó
  a fallback de Gemini para la respuesta final por 429 TPM. Más frágil
  que groq-oss-20b.
- openrouter-*: descartado para uso estable. Respuestas irregulares,
  tokens corruptos ("Cgasrea", "Persintar"), fechas inventadas en memoria,
  y créditos insuficientes en el plan pagado.

**Nota para sprints futuros:** para agregar un proveedor nuevo compatible
con OpenAI basta con heredar de OpenAICompatibleProvider y sobreescribir
3 propiedades. Agregar a la cadena de fallback solo requiere editar
appsettings.json o user-secrets, sin recompilar.

**Sprint:** post-MVP

---

### 2026-07-06 — Cadena de fallback extendida: gemini-3.1-flash-lite → gemini-2.5-flash → gemini-2.0-flash → groq-qwen-32b

**Contexto:** La cadena previa (`gemini-3.1-flash-lite → gemini-default → groq-qwen-32b`) solo tenía un escalón Gemini intermedio. Si gemini-3.1-flash-lite fallaba y gemini-default también, se saltaba directamente a Groq, que es más propenso a 429 TPM con contextos grandes.

**Decisión:** Se intercaló un nuevo perfil `gemini-2.0-flash` entre `gemini-default` (gemini-2.5-flash) y `groq-qwen-32b`. La cadena completa queda:
- `gemini-3.1-flash-lite` → `gemini-default` (gemini-2.5-flash) → `gemini-2.0-flash` → `groq-qwen-32b`

Cada escalón Gemini es un modelo distinto de la misma familia, lo que maximiza la probabilidad de éxito antes de cambiar de proveedor. Los fallos transitorios (502, rate limits) suelen ser por modelo específico, no por todo el servicio Gemini.

**Razón:** Darle a Gemini 3 oportunidades en vez de 2 antes de caer a Groq, basado en la observación de que los fallos de Gemini suelen ser por modelo individual (cuota, disponibilidad regional del endpoint), no por caída general del servicio. groq-qwen-32b queda como último recurso, no como fallback inmediato.
**Sprint:** post-MVP

---

### 2026-07-06 — Idempotencia en operación Add del MemoryPatchEngine

**Contexto:** Se detectó que los turnos de conversación no son atómicos:
si el modelo llama guardar_memoria exitosamente pero la respuesta final
falla (502, 429, etc.), al reintentar el usuario puede provocar que el
modelo repita el Add, creando elementos duplicados en arrays de memoria.
Observado en prueba con gemini-3.1-flash-lite: MemoryChange id=25/26
fueron escritos por un turno que terminó en 502, y al reintentar se
crearon id=27/28 con el mismo contenido.

**Decisión:** Opción B (idempotencia por patch) en vez de Opción A
(transacción por turno completo). La memoria escrita antes del fallo es
válida y debe conservarse — revertirla sería perder progreso real del
estudiante. Las transacciones largas sobre SQLite durante llamadas a
Gemini (segundos a decenas de segundos) son además arquitectónicamente
frágiles para un sistema de un solo usuario.

**Comportamiento nuevo por tipo de array:**
- mapa_dominio /temas: Add duplicado → upsert (merge parcial de campos,
  no duplica el elemento).
- lagunas_o_errores /activas: Add duplicado → no-op (laguna ya
  registrada, no se vuelve a insertar).
- historial_actividades /proyectos: Add duplicado → no-op.
- Set/Update en memoria_sesion y perfil_estudiante: ya eran idempotentes,
  sin cambio.

En todos los casos de upsert/no-op se registra un MemoryChange con el
Reason original más una nota indicando que fue convertido por
idempotencia, para mantener trazabilidad completa en auditoría.

**Sprint:** post-MVP

---

### 2026-07-03 — Bug crítico: chat cargaba todos los mensajes sin filtro por sesión

**Contexto:** Al revisar el Network tab del navegador se detectó que el
componente de chat llamaba GET /api/messages (endpoint genérico sin
filtro) en vez de filtrar por sesión activa. Esto causaba que se
cargaran TODOS los mensajes de TODAS las sesiones de TODOS los tutores
cada vez que el usuario seleccionaba una sesión — 214 mensajes en la
sesión de prueba observada, pero el número real era aún mayor porque
incluía mensajes de otras sesiones.

**Causa raíz:** el frontend usó el endpoint de administración/debugging
GET /api/messages (creado en el Sprint 2 del backend para exploración
vía Swagger) como si fuera el endpoint de chat real. El backend nunca
expuso un endpoint de mensajes filtrado por sesión con paginación hasta
este fix.

**Corrección aplicada:**
- Backend: nuevo endpoint GET /api/sessions/{sessionId}/messages con
  paginación (page, pageSize), orden DESC por CreatedAtUtc, 404 si la
  sesión no existe. Reutiliza PagedResult<T> ya existente.
- Frontend: ChatComponent reemplazó la llamada a GET /api/messages por
  GET /api/sessions/{sessionId}/messages?page=1&pageSize=50, con scroll
  infinito inverso (IntersectionObserver + sentinel al tope, prepend de
  páginas anteriores preservando posición de scroll).
- GET /api/messages sigue existiendo solo como endpoint admin/debug,
  no debe usarse en el flujo normal del chat.

**Verificado:** Network tab confirmado en navegador — ya no aparece
GET /api/messages al seleccionar sesiones. 25/25 tests pasando.

**Lección para sprints futuros:** cuando frontend y backend se
desarrollan en proyectos separados con OpenCode, verificar
explícitamente en el Network tab que los endpoints usados son los
correctos antes de dar por terminado cualquier sprint que involucre
carga de datos. Los endpoints de admin/debug del backend no deben
usarse en el flujo de la aplicación.

**Sprint:** post-MVP
