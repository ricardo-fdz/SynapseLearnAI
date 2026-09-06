# Synapse Learn AI — MVP v0.1.0

Plataforma de aprendizaje asistido por IA con tutores configurables, sesiones de estudio, memoria persistente gestionada por el propio agente vía Tool Calls, y auditoría completa. **MVP single-user, SQLite local**.

> Estado: hardening sprint completado (49/49 tests, 2026-09-05). Backlog activo vacío. Listo para demo/portfolio. Ver `docs/backlog.md` y `docs/archive/hallazgos-resueltos.md`.

## Arquitectura

```
Angular 21 (frontend :4200) → .NET 10 API (:5017) → SQLite
                               ├── TutorService / SessionService
                               ├── PromptBuilder (ContextLoadProfile)
                               ├── MemoryPatchEngine (Set/Add/Update/Resolve)
                               ├── GeminiProvider (+ Groq/OpenRouter via ILLMProvider)
                               └── MemoryChange audit
```

- **6 claves de memoria por tutor** (`esquemas-memoria.md`): `perfil_estudiante`, `memoria_sesion`, `mapa_dominio` (`temas`|`habilidades`), `lagunas_o_errores`, `historial_actividades`, `roadmap` (opcional, guía)
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
cd backend && dotnet test          # 49/49 OK (MemoryPatchEngine + PromptBuilder + H-013/P2/D-001)
cd backend && dotnet build LearningAgents.slnx -c Release
cd frontend && npm run build       # Angular 21
```

CI: `.github/workflows/ci.yml` (backend + frontend) en cada push a `main`.

## Docker (prod local)

```bash
# requiere GEMINI_API_KEY en env
docker compose up --build          # api :5017 + web :4200 (nginx proxy /api)
# frontend prod usa apiUrl='' (same-origin) via nginx.conf
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

## Estado del hardening (2026-09-05)

Resueltos y validados E2E: `H-001/003` Tracker persistencia, `H-002` `RowVersion` concurrencia, `H-006/007` semilla `habilidades` vs `temas`, `H-008/009/010` calidad pedagógica, `H-011` `siguiente_tema`/`proximo_paso` (ya renderiza ambos), `H-013` + `P2` escalado (cap 30 + resumen + cache 30m), `D-001` `Goal` informativo inyectado (`Meta declarada...`), `P3` infra (CI, Docker, `environment.prod.ts`).

Backlog activo vacío (`docs/backlog.md` solo `H-012` cerrado won't-fix). Histórico en `docs/archive/hallazgos-resueltos.md`.

## Seguridad

No commitees `*.db`, `*.db-shm`, `appsettings.Development.json` con secrets, `.env`. Usa `dotnet user-secrets` o variables `Gemini__ApiKey`, `Groq__ApiKey`. `.gitignore` ya cubre `**/bin/`, `**/obj/`, `**/*.db`.
