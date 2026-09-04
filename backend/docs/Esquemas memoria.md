# Esquemas JSON de `ValueJson` — Las 5 claves de MemoryEntry

> **Versión: v2** — Este documento es la fuente de verdad del modelo de memoria.
> El Memory Patch Engine, las tool declarations y los prompts deben alinearse a
> estos esquemas. Cambios respecto a v1 listados en
> [Resumen de cambios v2](#resumen-de-cambios-v2).

Este documento define la estructura interna del campo `ValueJson` para cada
una de las 5 claves estándar. Estos esquemas se derivan de los documentos
`.docx` que Ricardo usaba en un Gemini Gem, traducidos de prosa libre a JSON
con paths atómicos direccionables (para que el Memory Patch Engine pueda
operar con `Set/Add/Update/Resolve` sin reescribir el documento completo).

Todos los ejemplos abajo son datos de muestra, no el seed real.

---

## 1. `perfil_estudiante`

Estructurado en campos atómicos pequeños (según preferencia de Ricardo),
sacrificando la prosa libre original a cambio de precisión para el Patch
Engine. El único campo que conserva texto narrativo es `notas_tutor`, porque
es inherentemente cualitativo y de longitud variable — pero incluso ahí se
separa en entradas con fecha en vez de un párrafo único acumulativo.

Incluye `diagnostico_nivel`: **fuente única del nivel global del estudiante**
según el dominio del tutor. Es un snapshot de la evaluación más reciente; su
cadencia de actualización es distinta a la del resto del perfil (ver
[Regla de actualización](#regla-de-actualizacion)).

```json
{
  "alias": "Ricky",
  "lenguaje_principal": "DOTNET/Angular",
  "objetivo_declarado": "proyectos personales",
  "estilo_aprendizaje": {
    "prefiere": "combinacion",
    "ritmo_sesion": "cortas_intensas",
    "reaccion_ante_errores": "se_frustra_rapido",
    "nivel_autonomia": "pistas_minimas"
  },
  "preferencias_comunicacion": {
    "idioma": "espanol",
    "tono_tutor": "estricto_directo"
  },
  "diagnostico_nivel": {
    "area": "Programacion .NET",
    "escala": "niveles internos 1-5",
    "nivel": "Avanzado Inicial en Arquitectura de Software",
    "fecha_diagnostico": "2026-06-23",
    "resumen": "Domina la sintaxis y los patrones, pero necesita consolidar principios de diseno.",
    "brechas": [
      "Direccion de interfaces de repositorio",
      "Separacion fisica de proyectos en Clean Architecture"
    ],
    "siguiente_paso": "Practicar separacion fisica de proyectos en Clean Architecture"
  },
  "notas_tutor": [
    {
      "fecha": "2026-06-12",
      "nota": "Responde muy bien a retos de optimizacion. Su perfil evoluciono de programador de sintaxis a disenador de flujos eficientes."
    },
    {
      "fecha": "2026-06-23",
      "nota": "Estilo de aprendizaje virando hacia Arquitectura de Sistemas. Le interesa mas el como conecto las piezas que la sintaxis pura."
    }
  ],
  "ultima_actualizacion": "2026-06-23"
}
```

El sub-objeto `diagnostico_nivel` es **libre por dominio del tutor**: cada
prompt define su propia estructura interna (el ejemplo muestra `area/escala/
nivel/resumen/brechas/siguiente_paso`; un tutor de idiomas puede usar
`escala: MCER A1-C2` y campos por habilidad). El Patch Engine solo exige que
exista y sea un objeto; solo valida con rigidez cuando el prompt así lo
indica.

**Paths típicos para el Patch Engine:**
- `Update /estilo_aprendizaje/ritmo_sesion`
- `Add /notas_tutor` (nueva entrada con fecha)
- `Update /objetivo_declarado`
- `Set /diagnostico_nivel` (reemplazo completo del snapshot tras evaluación)

> **Nota engine**: el Patch Engine solo soporta un nivel de anidamiento para
> `estilo_aprendizaje` y `preferencias_comunicacion`. `diagnostico_nivel` se
> actualiza siempre como reemplazo completo (`Set /diagnostico_nivel`) — nunca
> por paths internos tipo `/diagnostico_nivel/brechas`.

<a name="regla-de-actualizacion"></a>
**Regla de actualización** (heredada del .docx original, con división de cadencia):
- `diagnostico_nivel` y sus `brechas`: se actualizan tras **cada evaluación
  formal** (nunca se espera 4-6 sesiones; es el dato vivo del nivel).
- Resto del perfil (`estilo_aprendizaje`, `preferencias_comunicacion`,
  `objetivo_declarado`, `notas_tutor`): cada **4-6 sesiones**, o
  inmediatamente si el usuario expresa explícitamente una preferencia distinta
  (ej. "prefiero menos analogías").
- `ultima_actualizacion` se refresca en cada escritura sobre esta clave.

---

## 2. `mapa_dominio`

Corresponde al "Mapa de Confianza por Tema" del Gem. Guarda el estado de
dominio por tema (o habilidad) y es **el eje para decidir qué sigue**.

El array contenedor puede llamarse **`temas`** (dominios técnicos) o
**`habilidades`** (tutores de idiomas: speaking/reading/listening/writing).
Cada tutor usa **uno solo** de los dos, según lo que defina su prompt. El
Prompt Builder y el Patch Engine deben aceptar ambos por igual.

`nivel` acepta **número (escala 1-3)** para tutores técnicos **o string**
(escala MCER, p. ej. `"B1"`) para idiomas. El tipo depende del prompt del
tutor; el Patch Engine valida que sea número o string, pero no impone un rango.

### 2a. Variante `temas` (dominios técnicos)

```json
{
  "temas": [
    {
      "id": "tema-clean-architecture-dotnet",
      "nombre": "Clean Architecture en .NET",
      "nivel": 3,
      "ultima_evaluacion": "2026-06-12",
      "notas": "Demostro dominio en examen practico, invirtiendo dependencias y aislando el dominio."
    },
    {
      "id": "tema-di",
      "nombre": "Inyeccion de Dependencias",
      "nivel": 3,
      "ultima_evaluacion": "2026-04-23",
      "notas": "Capacidad para elegir y justificar ciclos de vida y aplicar interfaces."
    },
    {
      "id": "tema-angular-signals",
      "nombre": "Angular Signals (Computed vs Effect)",
      "nivel": 1,
      "ultima_evaluacion": "2026-05-06",
      "notas": "Comenzo a conectar conceptos."
    }
  ]
}
```

**Niveles (escala estándar técnicas):**
| Nivel | Significado |
|-------|--------------|
| 1 | Visto — expuesto al concepto, no puede aplicarlo solo |
| 2 | Entiende — puede explicarlo con sus propias palabras |
| 3 | Aplica — puede usarlo sin ayuda en un proyecto nuevo |

### 2b. Variante `habilidades` (tutores de idiomas)

```json
{
  "habilidades": [
    {
      "id": "habilidad-speaking",
      "nombre": "Speaking",
      "nivel": "B1",
      "ultima_evaluacion": "2026-06-23",
      "notas": "Fluidez suficiente en conversacion casual; se traba en vocabulario tecnico."
    },
    {
      "id": "habilidad-listening",
      "nombre": "Listening",
      "nivel": "B2",
      "ultima_evaluacion": "2026-06-23",
      "notas": "Comprende material auditivo nativo si no hay jerga especializada."
    }
  ]
}
```

Los campos de cada ítem son idénticos a `temas`: `id, nombre, nivel,
ultima_evaluacion, notas`. Un tutor de idiomas puede además registrar
sub-habilidades (p. ej. `nivel: "B1"` para speaking y `"A2"` para
pronunciación) dentro del mismo array como ítems independientes con `id`
descriptivos.

**Paths típicos:**
- `Add /temas` o `Add /habilidades` (tema/habilidad nuevo)
- `Update /temas/{targetId}/nivel` o `Update /habilidades/{targetId}/nivel`
  (subir o bajar nivel)

**Regla de actualización** (heredada): subir el nivel solo si se demostró en
evaluación o proyecto real, no en ejercicios guiados. Bajar el nivel si en
sesión posterior falla consistentemente en ese tema. La decisión de qué tema
atacar en la siguiente sesión se registra en `memoria_sesion.siguiente_tema`.

---

## 3. `lagunas_o_errores`

Corresponde al "Registro de Lagunas Ocultas". Una laguna oculta es un
concepto donde el usuario domina la sintaxis pero no el mecanismo interno.
**Nunca se eliminan entradas**, solo cambian de estado.

Estados:
- `activas` — lagunas vigentes.
- `resueltas` — lagunas superadas (se conservan para auditoría y patrón).
- **Reactivación**: si una laguna resuelta reaparece, vuelve a `activas` y su
  `veces_visto` (o `veces_vista`) se incrementa — la entrada nunca se borra.

Cada entrada registra `veces_visto` para soportar **análisis de patrones y
fosilización**: una laguna con alta recurrencia indica un concepto que no se
consolidó y que el tutor debe atacar con un enfoque distinto.

```json
{
  "activas": [
    {
      "id": "laguna-clean-interfaces-001",
      "concepto": "Direccion de interfaces",
      "descripcion": "Confunde donde deben vivir las interfaces de repositorio.",
      "fecha_detectada": "2026-06-23",
      "veces_visto": 2
    }
  ],
  "resueltas": [
    {
      "id": "laguna-mutacion-colecciones",
      "concepto": "Mutacion de colecciones en iteracion",
      "descripcion": "Intentaba modificar listas dentro de foreach sin entender la corrupcion del iterador.",
      "fecha_detectada": "2026-04-22",
      "veces_visto": 1,
      "fecha_resolucion": "2026-04-23",
      "como_se_resolvio": "Explicacion del mecanismo interno y aplicacion exitosa de la tecnica de for inverso."
    }
  ]
}
```

**Paths típicos** (coinciden con los patches que ya definiste):
- `Add /activas` (nueva laguna detectada; `veces_visto` inicial = 1)
- `Resolve /activas` con `targetId` → el Patch Engine mueve la entrada de
  `activas` a `resueltas`, agregando `fecha_resolucion` y `como_se_resolvio`
- **Reactivación**: si una laguna de `resueltas` reaparece, el engine la
  devuelve a `activas` e incrementa su `veces_visto` (o si el modelo la
  re-registra con el mismo `id`, el engine detecta la colisión y la trata
  como reactivación en vez de error).

**Nota de diseño importante:** el patch de ejemplo que ya definiste usa
`operation: resolve` con `path: /activas` y un `targetId`. Esto implica que
el Memory Patch Engine necesita lógica especial para esta clave: no es un
`Update` simple sobre un path, es un **mover entre arrays** (de `activas` a
`resueltas` y viceversa en reactivación).

**Frontera con `perfil_estudiante.diagnostico_nivel.brechas` (regla v2):**
- `brechas` = **snapshot** de la evaluación actual (se sobreescribe en cada
  evaluación).
- `lagunas_o_errores` = registro **persistente y recurrente** (append-only).
- **Regla de promoción**: una brecha confirmada en **≥2 sesiones** se promueve
  a laguna activa. A la inversa, una brecha puntual detectada en una sola
  evaluación no crea laguna automáticamente.
- En una sesión, el tutor puede registrar la observación en ambos lugares
  (brecha como snapshot y laguna si ya es recurrente), pero la laguna es la
  que guía la reintroducción del concepto.

---

## 4. `memoria_sesion`

**Modelo confirmado: estado único mutable (checkpoint).** A diferencia del
log histórico de sesiones del `.docx` original, esta clave guarda solo el
estado de la sesión más reciente — se sobreescribe en cada sesión, no acumula
entradas. El historial de progreso real ya queda cubierto por `mapa_dominio`
(niveles por tema) e `historial_actividades` (proyectos completados), así que
esta clave no necesita duplicar esa función; su único trabajo es decirle al
tutor "dónde quedamos la última vez" al abrir una nueva sesión.

**Momento de escritura:**
- **Al abrir la sesión** — el tutor escribe un checkpoint ligero (tema a
  retomar, siguiente_paso) para tener punto de partida si la sesión se corta.
- **Al cerrar la sesión** — el tutor sobreescribe el checkpoint con el cierre:
  qué se dominó, qué ejercicio se hizo, tiempo invertido y qué sigue.

```json
{
  "fecha_ultima_sesion": "2026-06-23",
  "temas_dominados_ultima_sesion": [
    "Principio de Inversion de Dependencias (IoC) aplicado a fronteras de proyectos",
    "Regla de Dependencia en Clean Architecture"
  ],
  "ultimo_ejercicio": "Refactorizacion de un monolito conceptual simulando las 4 capas de Clean Architecture",
  "tiempo_invertido_minutos": 45,
  "siguiente_tema": "tema-clean-interfaces",
  "proximo_paso": "Practicar separacion fisica de proyectos en Clean Architecture"
}
```

**Campos (todos en la raíz, sin array intermedio):**
- `fecha_ultima_sesion` — fecha del cierre (o apertura en checkpoint ligero).
- `temas_dominados_ultima_sesion` — resumen propio de la sesión. No es
  redundante con `mapa_dominio`: ahí están niveles 3 "confirmados"; aquí es
  el recuento inmediato de la sesión.
- `ultimo_ejercicio` — descripción breve del ejercicio/actividad realizada.
- `tiempo_invertido_minutos` — duración estimada de la sesión.
- `siguiente_tema` — **decisión de ruta**: `id` (o nombre) del próximo tema
  del `mapa_dominio` a atacar. Es el vínculo con el mapa.
- `proximo_paso` — **acción concreta ejecutable** para la próxima sesión
  (distinto de `siguiente_tema`: éste dice *qué tema*, `proximo_paso` dice
  *qué acción hacer con él*).

**Paths típicos** (todos sobre la raíz):
- `Set /proximo_paso`
- `Set /siguiente_tema`
- `Set /fecha_ultima_sesion`
- `Set /temas_dominados_ultima_sesion` (reemplazo de la lista de la sesión)

Como todos los campos viven directamente en la raíz del objeto (sin un array
contenedor), cada patch de tipo `Set` simplemente reemplaza el valor de un
campo puntual — el caso más simple de los 5 esquemas para el Patch Engine.

> **Eliminado en v2:** `nivel_actual`. Provocaba drift (duplicaba
> `perfil_estudiante.diagnostico_nivel.nivel`). La fuente única del nivel
> global es `diagnostico_nivel`.

---

## 5. `historial_actividades`

Corresponde al "Historial de Proyectos Completados". Append-only — nunca se
modifican entradas anteriores, solo se agregan nuevas.

`problemas_encontrados` es un **snapshot temporal** de la actividad. Si el
problema resulta **persistente o recurrente**, el tutor **lo promueve** a una
laguna en `lagunas_o_errores` (vía la regla de promoción de la sección 3); no
se duplica el registro completo en ambas claves.

```json
{
  "proyectos": [
    {
      "id": "proyecto-api-inventario-001",
      "nombre": "API de Inventario (Servicio de Calculo de Riesgo Financiero)",
      "fecha": "2026-04-27",
      "temas_integrados": [
        "Inyeccion de Dependencias",
        "LINQ Avanzado",
        "Entity Framework Core",
        "Programacion Asincrona"
      ],
      "nivel_ayuda_requerido": "pistas_minimas",
      "problemas_encontrados": "Inicialmente se descargo toda la tabla a memoria (ToListAsync) antes de sumar.",
      "resultado": "completado",
      "observaciones_tutor": "Demostro excelente intuicion al deducir SumAsync(). Refactorizo hacia ejecucion diferida tras feedback."
    }
  ]
}
```

**Paths típicos:**
- `Add /proyectos` (nuevo proyecto completado o abandonado)
- Si un proyecto se retoma, se agrega una **nueva entrada** con un campo
  `proyecto_relacionado_id` apuntando al original — nunca se edita el
  original (regla heredada del .docx).

---

## Reglas transversales v2

Estas reglas aplican a todas las claves y alinean engine, tools y prompts.

1. **Fuente única del nivel global**: `perfil_estudiante.diagnostico_nivel.nivel`.
   `memoria_sesion` no lleva nivel propio (se eliminó `nivel_actual`).
2. **Frontera brechas vs lagunas**: `brechas` = snapshot de evaluación (se
   sobreescribe); `lagunas_o_errores` = persistente/recurrente (append-only).
   Promoción de brecha a laguna tras **≥2 sesiones** de persistencia.
3. **Frontera `siguiente_tema` vs `proximo_paso`**: uno dice *qué tema
   atacar*, el otro *qué acción concreta*, ambos en `memoria_sesion`.
4. **`nivel` heterogéneo en `mapa_dominio`**: número (1-3) para tutores
   técnicos, string (MCER, etc.) para idiomas. Engine y tool declarations
   aceptan ambos; el prompt del tutor define cuál usa.
5. **`mapa_dominio`: `temas` o `habilidades`**, no ambos. Cada tutor elige
   uno según su prompt. Tools y engine soportan `Add/Update` sobre cualquiera
   de los dos arrays.
6. **`value` heterogéneo en tools**: un patch puede llevar `value` de tipo
   número, string, array u objeto. El schema de `guardar_memoria` no debe
   restringirlo a `object`.
7. **`SchemaVersion`**: se mantiene en `1`. Si una futura versión rompe la
   estructura de un esquema, se **incrementa el campo y se migra** el
   `ValueJson` en la migración correspondiente. Un cambio aditivo o de
   comportamiento (como v2) mantiene `1`.

---

## Resumen de cambios v2

| Clave | Cambio |
|-------|--------|
| `perfil_estudiante` | Documenta `diagnostico_nivel` como fuente única del nivel; cadencia dividida (diagnóstico tras cada evaluación, resto 4-6 sesiones). |
| `mapa_dominio` | Soporta variantes `temas` \| `habilidades`; `nivel` heterogéneo (número o MCER/string). |
| `lagunas_o_errores` | Nuevo campo `veces_visto`; soporta reactivación (resuelta → activa con incremento); regla de promoción desde `brechas`. |
| `memoria_sesion` | Eliminado `nivel_actual`; nuevo `siguiente_tema`; escritura al abrir (checkpoint ligero) y al cerrar. |
| `historial_actividades` | `problemas_encontrados` como snapshot temporal; promoción a laguna para lo persistente. |