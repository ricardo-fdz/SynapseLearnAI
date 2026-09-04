using System.Text.Json;
using LearningAgents.Domain.LLM;

namespace LearningAgents.Application.Services;

internal static class MemoryToolDeclarations
{
    public static IReadOnlyList<LLMToolDeclaration> All =>
    [
        new(
            "leer_memoria",
            "Lee el ValueJson de una MemoryEntry del tutor actual por key.",
            Json("""
            {
              "type": "object",
              "properties": {
                "key": {
                  "type": "string",
                  "enum": ["memoria_sesion", "perfil_estudiante", "mapa_dominio", "lagunas_o_errores", "historial_actividades"]
                }
              },
              "required": ["key"]
            }
            """)),
        new(
            "guardar_memoria",
            """
            Aplica un MemoryPatch validado sobre la memoria persistente del tutor actual. Si apruebas formalmente al estudiante en un tema, usa guardar_memoria antes de la respuesta final para registrar el dominio en mapa_dominio. Antes de usar operation Update o Resolve sobre un array con elementos identificables (mapa_dominio, lagunas_o_errores), primero llama a leer_memoria con la key correspondiente para obtener el 'id' real de cada elemento; nunca uses el nombre, concepto o titulo visible como targetId.

            mapa_dominio usa SIEMPRE UNO de los dos arrays: '/temas' para dominios técnicos o '/habilidades' para tutores de idiomas (speaking, listening, reading, writing). Usa el que defina el prompt de tu tutor; nunca mezcles ambos.

            Ejemplo correcto para mapa_dominio Add tras evaluacion aprobada (tutor tecnico):
            {
              "patch": {
                "key": "mapa_dominio",
                "operation": "Add",
                "path": "/temas",
                "value": {
                  "id": "tema-clean-code-srp",
                  "nombre": "Clean Code: responsabilidad unica",
                  "nivel": 3,
                  "notas": "Aprobó evaluación formal aplicando SRP en un ejercicio práctico",
                  "ultima_evaluacion": "2026-07-02"
                },
                "reason": "Evaluacion formal aprobada"
              }
            }

            Ejemplo correcto para mapa_dominio Add (tutor de idiomas, escala MCER):
            {
              "patch": {
                "key": "mapa_dominio",
                "operation": "Add",
                "path": "/habilidades",
                "value": {
                  "id": "habilidad-speaking",
                  "nombre": "Speaking",
                  "nivel": "B1",
                  "notas": "Fluidez suficiente en conversacion casual",
                  "ultima_evaluacion": "2026-07-02"
                },
                "reason": "Evaluacion de habilidad completada"
              }
            }

            Ejemplo correcto para mapa_dominio Update:
            {
              "patch": {
                "key": "mapa_dominio",
                "operation": "Update",
                "targetId": "tema-clean-architecture-dotnet",
                "path": "/temas/nivel",
                "value": 3,
                "reason": "Demostró aplicación práctica sin ayuda en evaluación formal"
              }
            }
            targetId es el campo 'id' del tema/habilidad (obtenido via leer_memoria), NUNCA su 'nombre'. path siempre es '/temas/NOMBRE_DEL_CAMPO' o '/habilidades/NOMBRE_DEL_CAMPO' apuntando a UN solo campo (nivel O notas, nunca ambos en la misma llamada); nunca el nombre del tema ni el array completo. Para idiomas, '/habilidades/NOMBRE_DEL_CAMPO' (ej. '/habilidades/nivel').

            Ejemplo correcto para guardar el alias del estudiante al inicio de la sesion:
            {
              "patch": {
                "key": "perfil_estudiante",
                "operation": "Set",
                "path": "/alias",
                "value": "Juan",
                "reason": "El estudiante se presento al inicio de la sesion"
              }
            }

            Ejemplo correcto para actualizar memoria_sesion al cierre de la sesion. Usa Set sobre UN campo valido por llamada; no intentes guardar un objeto completo de resumen:
            {
              "patch": {
                "key": "memoria_sesion",
                "operation": "Set",
                "path": "/proximo_paso",
                "value": "Practicar mediciones seguras de voltaje y corriente con multimetro",
                "reason": "Cierre de sesion con siguiente paso recomendado"
              }
            }
            Campos validos de memoria_sesion: /fecha_ultima_sesion, /temas_dominados_ultima_sesion, /ultimo_ejercicio, /tiempo_invertido_minutos, /siguiente_tema, /proximo_paso. /siguiente_tema guarda el 'id' del proximo tema/habilidad del mapa_dominio a atacar; /proximo_paso guarda la accion concreta. No uses 'nivel_actual' en memoria_sesion: el nivel global vive en perfil_estudiante.diagnostico_nivel.

            Ejemplo correcto para guardar diagnostico inicial de nivel en perfil_estudiante.
            Usa una estructura propia del dominio del tutor; no uses campos de idiomas (reading, writing, listening, speaking) salvo que el tutor sea especificamente de idiomas:
            {
              "patch": {
                "key": "perfil_estudiante",
                "operation": "Set",
                "path": "/diagnostico_nivel",
                "value": {
                  "area": "Electronica basica",
                  "escala": "niveles internos 1-5",
                  "nivel": 2,
                  "resumen": "Comprende la relacion entre voltaje, corriente y resistencia, pero necesita consolidar unidades y notacion tecnica",
                  "evidencias": [
                    "Explico la relacion inversa entre resistencia y corriente",
                    "Propuso medir bateria e interruptor para diagnosticar una falla"
                  ],
                  "brechas": [
                    "Precision con prefijos metricos como k y m",
                    "Formalizacion de calculos con Ley de Ohm"
                  ],
                  "siguiente_paso": "Practicar mediciones seguras con multimetro y circuitos serie simples"
                },
                "reason": "Diagnostico inicial completado con evidencia del dominio especifico"
              }
            }

            Ejemplo correcto para lagunas_o_errores Resolve:
            {
              "patch": {
                "key": "lagunas_o_errores",
                "operation": "Resolve",
                "targetId": "laguna-clean-interfaces-001",
                "path": "/activas",
                "value": {
                  "fecha_resolucion": "2026-06-25",
                  "como_se_resolvio": "Explicó correctamente la regla de dependencia"
                },
                "reason": "El estudiante demostró comprensión correcta del concepto"
              }
            }
            targetId es el campo 'id' de la laguna activa (obtenido via leer_memoria), NUNCA su 'concepto' o titulo visible.

            Ejemplo correcto para registrar una nueva laguna activa. veces_visto inicia en 1 y lo incrementa el engine si una laguna resuelta reaparece (reactivacion):
            {
              "patch": {
                "key": "lagunas_o_errores",
                "operation": "Add",
                "path": "/activas",
                "value": {
                  "id": "laguna-directores-interfaces-002",
                  "concepto": "Direccion de interfaces",
                  "descripcion": "Confunde donde deben vivir las interfaces de repositorio",
                  "fecha_detectada": "2026-07-02"
                },
                "reason": "Error recurrente detectado durante la sesion"
              }
            }

            Ejemplo correcto para historial_actividades Add (actividad/proyecto completado):
            {
              "patch": {
                "key": "historial_actividades",
                "operation": "Add",
                "path": "/proyectos",
                "value": {
                  "id": "proyecto-api-inventario-001",
                  "nombre": "API de Inventario",
                  "fecha": "2026-07-02",
                  "temas_integrados": ["Inyeccion de Dependencias", "EF Core"],
                  "nivel_ayuda_requerido": "pistas_minimas",
                  "problemas_encontrados": "Descargo toda la tabla a memoria antes de sumar",
                  "resultado": "completado",
                  "observaciones_tutor": "Demostro intuicion al deducir SumAsync()"
                },
                "reason": "Actividad completada en la sesion"
              }
            }
            """,
            Json("""
            {
              "type": "object",
              "properties": {
                "patch": {
                  "type": "object",
                  "properties": {
                    "key": {
                      "type": "string",
                      "enum": ["memoria_sesion", "perfil_estudiante", "mapa_dominio", "lagunas_o_errores", "historial_actividades"]
                    },
                    "operation": {
                      "type": "string",
                      "enum": ["Set", "Add", "Update", "Resolve"]
                    },
                    "path": { "type": "string" },
                    "targetId": { "type": "string" },
                    "value": {
                      "oneOf": [
                        { "type": "string" },
                        { "type": "number" },
                        { "type": "boolean" },
                        { "type": "array" },
                        { "type": "object" }
                      ],
                      "description": "Valor del campo a fijar/agregar/actualizar. Puede ser un numero (ej. nivel 1-3), un string (ej. proximo_paso), una lista (ej. temas_dominados_ultima_sesion) o un objeto (ej. diagnostico_nivel, nueva laguna, nuevo tema/proyecto)."
                    },
                    "reason": {
                      "type": "string",
                      "description": "Motivo obligatorio para la aplicacion. Si falta, guardar_memoria rechazara el patch y pedira corregirlo."
                    }
                  },
                  "required": ["key", "operation", "path", "value", "reason"]
                }
              },
              "required": ["patch"]
            }
            """)),
        new(
            "listar_memoria",
            "Lista las 5 keys de memoria disponibles e indica si cada una tiene contenido o esta vacia, sin devolver contenido completo.",
            Json("""
            {
              "type": "object",
              "properties": {}
            }
            """))
    ];

    private static JsonElement Json(string json) => JsonSerializer.Deserialize<JsonElement>(json);
}
