# Resultados de agentes LLM

Fecha de corte: 2026-07-03.

Este documento resume el rendimiento, consistencia y comportamiento de tool calling/memoria observado en las pruebas locales de LearningAgents.

## Resumen ejecutivo

| Perfil | Proveedor | Rendimiento observado | Consistencia | Memoria/tools | Veredicto |
| --- | --- | --- | --- | --- | --- |
| `gemini-default` | Gemini | Bueno | Alta | Correcto | Default estable |
| `groq-oss-20b` | Groq | Bueno, rapido cuando no hay rate limit | Media-alta | Correcto tras reforzar tool guidance | Experimental viable |
| `groq-qwen-32b` | Groq | Bueno en primer paso, sensible a TPM | Media | Guardo memoria, pero cayo a fallback para respuesta final | Fallback/experimental |
| `openrouter-default` | OpenRouter | Bloqueado por creditos | No evaluable | No evaluable | No usable sin creditos |
| `openrouter-free` | OpenRouter | Irregular | Baja-media | A veces correcto | No recomendado estable |
| `openrouter-gpt-oss-20b-free` | OpenRouter | Malo/irregular | Baja | Inestable/fallback | Descartado por ahora |
| `openrouter-qwen-coder-free` | OpenRouter | Limitado por `429` | Baja | Dependio de fallback | Descartado por ahora |

## Gemini

Gemini quedo como el proveedor mas estable para el flujo principal.

Resultados observados:

- Mejor consistencia pedagogica general.
- Buen seguimiento del prompt global y del prompt especifico del tutor.
- Menos salidas corruptas o respuestas fuera de estilo.
- `gemini-2.0-flash` produjo `429` en pruebas anteriores.
- `gemini-2.5-flash` funciono mejor como default/fallback.
- Los retries y fallback de `GeminiProvider` redujeron fallos visibles al usuario.

Veredicto:

- Mantener `gemini-default` como default estable.
- Usarlo para experiencia local/produccion cuando se prioriza consistencia.

## Groq OSS 20B

`groq-oss-20b` fue el proveedor alternativo mas prometedor.

Resultados observados:

- Buena calidad conversacional en sesiones Clean Code/SRP.
- Respuestas generalmente rapidas cuando no se alcanza el limite TPM.
- Tool calling funcional en pruebas de memoria.
- Despues de reforzar `MemoryToolDeclarations.cs`, registro correctamente `mapa_dominio` al aprobar una evaluacion formal.

Prueba confirmada:

- Tutor: `19`.
- Sesion: `31`.
- Perfil: `groq-oss-20b`.
- Resultado: aprobacion de SRP y guardado correcto en memoria.
- `MemoryChange`: `40`.
- Key: `mapa_dominio`.
- Operacion: `Add`.

Registro guardado:

```json
{
  "id": "tema-clean-code-srp",
  "nivel": 3,
  "nombre": "Clean Code: responsabilidad única",
  "notas": "Aprobó evaluación formal aplicando SRP en un ejercicio práctico",
  "ultima_evaluacion": "2026-07-03"
}
```

Riesgos:

- Puede devolver `429` por limite TPM.
- En una prueba anterior aprobo sin guardar memoria; se corrigio reforzando la descripcion de `guardar_memoria`.
- Conviene respetar cooldown entre turnos si se hacen pruebas manuales.

Veredicto:

- Viable como proveedor experimental.
- No recomendado como default aun por rate limits.

## Groq Qwen 32B

`groq-qwen-32b` se probo como alternativa Groq adicional.

Prueba confirmada:

- Tutor: `20`.
- Sesion: `32`.
- Perfil: `groq-qwen-32b`.
- Primer request a Groq: `200`.
- Qwen llamo `guardar_memoria` correctamente.
- `MemoryChange`: `41`.
- Key: `mapa_dominio`.
- Operacion: `Add`.
- Segundo request a Groq para respuesta final: `429` TPM.
- Fallback usado para respuesta final: `gemini-2.5-flash`.

Registro guardado:

```json
{
  "id": "tema-clean-code-srp",
  "nivel": 3,
  "nombre": "SRP en descompocision de funciones",
  "notas": "Aprobó evaluacion formal separando procesarPedido en 5 responsabilidades unicas con justificacion teorica",
  "ultima_evaluacion": "2026-07-02"
}
```

Observaciones:

- Tool calling funciono: Qwen guardo memoria antes de la respuesta final.
- La respuesta final no fue estrictamente de Qwen porque el segundo request recibio `429` y el router uso Gemini fallback.
- Hubo detalles de calidad en el valor guardado: typo en `descompocision` y fecha `2026-07-02` aunque la prueba fue el 2026-07-03.
- El limite TPM observado fue menor que el de `groq-oss-20b` en esta prueba: `qwen/qwen3-32b` reporto limite `6000` TPM.

Veredicto:

- Funciona para tool calling y puede guardar memoria.
- Es mas fragil que `groq-oss-20b` para sesiones completas por rate limit.
- Mantener como fallback/experimental, no como default.

## OpenRouter

OpenRouter fue el proveedor menos consistente en estas pruebas.

Resultados observados:

- `openrouter-default` con modelo pagado devolvio `402 Insufficient credits`.
- `openrouter-free` llego a ejecutar tools y guardar memoria, pero con calidad irregular.
- Modelos free especificos devolvieron respuestas sin texto, restos tipo `</think>`, negativas injustificadas o `429`.
- Se observaron tokens/textos raros como `Cgasrea`, `Persintar` y `neq??`.
- Una memoria guardada incluyo fecha inventada.

Casos relevantes:

- `MemoryChange id=37`: `openrouter-free` guardo memoria, pero la conversacion fue irregular.
- `MemoryChange id=38`: `openrouter-gpt-oss-20b-free` termino guardando via fallback.
- `MemoryChange id=39`: `openrouter-qwen-coder-free` dependio de fallback y guardo una fecha inventada.

Veredicto:

- Descartado por ahora como proveedor estable.
- Mantener solo para sandbox o experimentos puntuales.

## Ranking practico

1. `gemini-default`: mejor opcion general y default recomendado.
2. `groq-oss-20b`: mejor alternativa experimental; buena calidad y tool calling, pero sensible a rate limits.
3. `groq-qwen-32b`: tool calling correcto, pero mas fragil por TPM y detalles de calidad en memoria.
4. `openrouter-free`: puede funcionar puntualmente, pero no es confiable.
5. `openrouter-*` especificos free: descartados para flujo estable.

## Recomendacion actual

- Default: `gemini-default`.
- Alternativa experimental: `groq-oss-20b`.
- Fallback experimental: `groq-qwen-32b`.
- Evitar para usuarios finales: `openrouter-*`.
- Mantener los fallbacks del router activos porque recuperaron correctamente casos de `402`, `429` y respuestas vacias.
- Seguir probando Groq con cooldown y sesiones completas antes de considerarlo default.
