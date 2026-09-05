# Docs — SynapseLearnAI

Índice de documentación del mono-repo. Los docs **generales** viven aquí (single source); los docs **específicos de agente** quedan en `backend/docs/AGENTS.md` y `frontend/docs/AGENTS.md`.

| Archivo | Función | Fuente de verdad |
|---|---|---|
| `arquitectura.md` | Visión mono-repo: diagrama, stack, vertical slice | Extraído de `backend/docs/AGENTS.md:5` |
| `esquemas-memoria.md` | Contrato `ValueJson` de las 5 claves (`perfil_estudiante`, `memoria_sesion`, `mapa_dominio`, `lagunas_o_errores`, `historial_actividades`) — v2 | `MemoryPatchEngine.cs:66` + `PromptBuilder.cs:73` |
| `endpoints.md` | Referencia API (21 rutas auditadas `decisiones.md:331`) + flujos | Controllers + `Program.cs` |
| `backlog.md` | Backlog activo de hardening (H-002, H-011, D-001) | `hallazgos-hardening.md` previo |
| `decisiones.md` | Decisiones que afectan a ambos proyectos (ej. multi-provider) | — |
| `archive/hallazgos-resueltos.md` | Hallazgos resueltos H-001/003/004/005/006/007/008/009/010/013 | — |
| `archive/aprendizajes/2026-08-25-hardening.md` | Bitácora fechada del sprint | — |
| `archive/benchmarks/2026-07-03-llm.md` | Benchmark Gemini/Groq/OpenRouter | — |
| `archive/prompts/` | Rollbacks de prompts | — |

**Por proyecto:**
- `backend/docs/AGENTS.md` — spec backend para agentes (no duplicar aquí)
- `backend/docs/decisiones.md` — log backend (15 entradas)
- `backend/docs/prompts/` — `PROMPT_GLOBAL.md` canonical (copiado por `LearningAgents.Api.csproj:24`)
- `frontend/docs/AGENTS.md` — spec frontend Angular
- `frontend/docs/decisiones.md` — log frontend
