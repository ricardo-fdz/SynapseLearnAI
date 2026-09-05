# Synapse Learn AI — MVP v0.1.0

Plataforma de aprendizaje asistido por IA con tutores configurables, sesiones de estudio, memoria persistente gestionada por el propio agente vía Tool Calls, y auditoría completa. **MVP single-user, SQLite local**.

> Estado: hardening sprint completado (48/48 tests). Listo para demo/portfolio. Ver `backend/docs/hallazgos-hardening.md` para limitaciones conocidas.

## Arquitectura

```
Angular 21 (frontend :4200) → .NET 10 API (:5017) → SQLite
                               ├── TutorService / SessionService
                               ├── PromptBuilder (ContextLoadProfile)
                               ├── MemoryPatchEngine (Set/Add/Update/Resolve)
                               ├── GeminiProvider (+ Groq/OpenRouter via ILLMProvider)
                               └── MemoryChange audit
```

- **5 claves de memoria por tutor** (`AGENTS.md:6`): `perfil_estudiante`, `memoria_sesion`, `mapa_dominio` (`temas`|`habilidades`), `lagunas_o_errores`, `historial_actividades`
- **Tool Calls del agente:** `leer_memoria`, `guardar_memoria`, `listar_memoria` — el tutor actualiza su memoria autónomamente
- **Context Profiles** `PromptBuilder.cs:73`: `Standard` (perfil+sesión+mapa+lagunas), `Evaluation`, `Project`, `FullReview`

Docs técnicos: `docs/arquitectura.md`, `docs/esquemas-memoria.md`, `docs/endpoints.md`, `docs/backlog.md` · Specs agente: `backend/docs/AGENTS.md`, `frontend/docs/AGENTS.md`

## Requisitos

- Node.js 20+ / npm 10+
- .NET SDK 10
- Clave Gemini (o Groq/OpenRouter) — vía User Secrets (no en `appsettings.json`)

## Desarrollo local — inicio en 1 comando

```bash
# 1. Clonar y configurar LLM (una vez)
dotnet user-secrets set "Gemini:ApiKey" "tu-key" --project backend/src/LearningAgents.Api
npm install          # instala concurrently (root) + deps frontend vía install:all
npm run install:all  # alternativa: dotnet restore + npm ci

# 2. Iniciar todo (API :5017 + Web :4200) — elige una:
npm run dev          # concurrently con logs coloreados (recomendado)
./dev.sh             # bash sin dependencias extra
make dev             # alias a npm run dev
make dev-sh          # alias a ./dev.sh
```

Detalles por servicio (si prefieres manual):
```bash
dotnet run --project backend/src/LearningAgents.Api  # Swagger en /swagger
npm start --prefix frontend                            # http://localhost:4200
```

## Verificación

```bash
cd backend && dotnet test          # 48/48 OK (MemoryPatchEngine + PromptBuilder + H-013 scaling)
cd backend && dotnet build LearningAgents.slnx -c Release
cd frontend && npm run build
```

Ejemplo API:

```bash
curl -X POST http://localhost:5017/api/tutors -H "Content-Type: application/json" \
  -d '{"name":"Tutor JS","description":"...","systemPromptContent":"..."}'

curl -X POST http://localhost:5017/api/study-sessions -H "Content-Type: application/json" \
  -d '{"tutorId":1,"name":"Sesión 1","goal":"Repaso closures"}'

curl -X POST http://localhost:5017/api/sessions/1/messages -H "Content-Type: application/json" \
  -d '{"content":"Hola, quiero repasar closures","profile":"Standard"}'
```

## Estado del hardening (2026-09-04)

Resueltos y validados E2E: `H-001/003` Tracker persistencia, `H-006/007` semilla `habilidades` vs `temas`, `H-008/009/010` calidad pedagógica (tema puente, verificación, diagnóstico), `H-013` escalado prompt (cap 30 msgs + memoria truncada).

Pendiente post-MVP: `H-002` concurrencia sin `RowVersion` (last-write-wins, improbable single-user), `H-011` `siguiente_tema` vs `proximo_paso` cosmético. Ver `docs/backlog.md`.

## Seguridad

No commitees `*.db`, `*.db-shm`, `appsettings.Development.json` con secrets, `.env`. Usa `dotnet user-secrets` o variables `Gemini__ApiKey`, `Groq__ApiKey`. `.gitignore` ya cubre `**/bin/`, `**/obj/`, `**/*.db`.
