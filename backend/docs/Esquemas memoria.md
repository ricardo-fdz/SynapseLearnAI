# Esquemas JSON de `ValueJson` — Las 5 claves de MemoryEntry

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

**Paths típicos para el Patch Engine:**
- `Update /estilo_aprendizaje/ritmo_sesion`
- `Add /notas_tutor` (nueva entrada con fecha)
- `Update /objetivo_declarado`

**Regla de actualización** (heredada del .docx original): actualizar cada
4-6 sesiones, o inmediatamente si el usuario expresa explícitamente una
preferencia distinta (ej. "prefiero menos analogías").

---

## 2. `mapa_dominio`

Corresponde al "Mapa de Confianza por Tema" del Gem. Escala de 1-3
(Visto / Entiende / Aplica), tal como ya la tenías.

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

**Niveles:**
| Nivel | Significado |
|-------|--------------|
| 1 | Visto — expuesto al concepto, no puede aplicarlo solo |
| 2 | Entiende — puede explicarlo con sus propias palabras |
| 3 | Aplica — puede usarlo sin ayuda en un proyecto nuevo |

**Paths típicos:**
- `Add /temas` (tema nuevo)
- `Update /temas/{targetId}/nivel` (subir o bajar nivel)

**Regla de actualización** (heredada): subir el nivel solo si se demostró en
evaluación o proyecto real, no en ejercicios guiados. Bajar el nivel si en
sesión posterior falla consistentemente en ese tema.

---

## 3. `lagunas_o_errores`

Corresponde al "Registro de Lagunas Ocultas". Una laguna oculta es un
concepto donde el usuario domina la sintaxis pero no el mecanismo interno.
**Nunca se eliminan entradas**, solo cambian de estado.

```json
{
  "activas": [
    {
      "id": "laguna-clean-interfaces-001",
      "concepto": "Direccion de interfaces",
      "fecha_detectada": "2026-06-23",
      "descripcion": "Confunde donde deben vivir las interfaces de repositorio."
    }
  ],
  "resueltas": [
    {
      "id": "laguna-mutacion-colecciones",
      "concepto": "Mutacion de colecciones en iteracion",
      "fecha_detectada": "2026-04-22",
      "descripcion": "Intentaba modificar listas dentro de foreach sin entender la corrupcion del iterador.",
      "fecha_resolucion": "2026-04-23",
      "como_se_resolvio": "Explicacion del mecanismo interno y aplicacion exitosa de la tecnica de for inverso."
    }
  ]
}
```

**Paths típicos** (coinciden con los patches que ya definiste):
- `Add /activas` (nueva laguna detectada)
- `Resolve /activas` con `targetId` → el Patch Engine debe mover la entrada
  de `activas` a `resueltas`, agregando `fecha_resolucion` y
  `como_se_resolvio`

**Nota de diseño importante:** el patch de ejemplo que ya definiste usa
`operation: resolve` con `path: /activas` y un `targetId`. Esto implica que
el Memory Patch Engine necesita lógica especial para esta clave: no es un
`Update` simple sobre un path, es un **mover entre arrays** (de `activas` a
`resueltas`). Vale la pena anotar esto en el diseño del Sprint 5 para que
no se trate como un patch genérico.

---

## 4. `memoria_sesion`

**Modelo confirmado: Estado único mutable.** A diferencia del log histórico
de sesiones del `.docx` original, esta clave guarda solo el estado de la
sesión más reciente — se sobreescribe en cada sesión, no acumula entradas.
El historial de progreso real ya queda cubierto por `mapa_dominio` (niveles
por tema) e `historial_actividades` (proyectos completados), así que esta
clave no necesita duplicar esa función; su único trabajo es decirle al tutor
"dónde quedamos la última vez" al abrir una nueva sesión.

```json
{
  "fecha_ultima_sesion": "2026-06-23",
  "nivel_actual": "Avanzado Inicial en Arquitectura de Software (.NET)",
  "temas_dominados_ultima_sesion": [
    "Principio de Inversion de Dependencias (IoC) aplicado a fronteras de proyectos",
    "Regla de Dependencia en Clean Architecture"
  ],
  "ultimo_ejercicio": "Refactorizacion de un monolito conceptual simulando las 4 capas de Clean Architecture",
  "tiempo_invertido_minutos": 45,
  "proximo_paso": "Practicar separacion fisica de proyectos en Clean Architecture"
}
```

**Paths típicos** (todos sobre la raíz, sin array intermedio):
- `Set /proximo_paso`
- `Set /nivel_actual`
- `Set /fecha_ultima_sesion`
- `Update /temas_dominados_ultima_sesion`

Como todos los campos viven directamente en la raíz del objeto (sin un
array contenedor), cada patch de tipo `Set` simplemente reemplaza el valor
de un campo puntual — el caso más simple de los 5 esquemas para el Patch
Engine.

---

## 5. `historial_actividades`

Corresponde al "Historial de Proyectos Completados". Append-only — nunca se
modifican entradas anteriores, solo se agregan nuevas.

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

## Resumen de decisión

El modelo de `memoria_sesion` ya quedó confirmado (Modelo B, estado único
mutable). Con esto, los 5 esquemas están completos y listos para usarse
tanto en el Sprint 3 (Prompt Builder — renderizar cada uno a Markdown) como
en el Sprint 5 (Memory Patch Engine — validar y aplicar patches sobre estos
paths específicos).