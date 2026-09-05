# Arquitectura — SynapseLearnAI

> Extraído de `backend/docs/AGENTS.md:5` y `README.md:7`. Single source mono-repo.

```
Angular 21 (:4200)  →  .NET 10 API (:5017)  →  SQLite (learning-agents.db)
  core/state (Signals)    ├── TutorService / SessionService
  features/sidebar/chat   ├── PromptBuilder (ContextLoadProfile: Standard/Evaluation/Project/FullReview)
  shared/components       ├── MemoryPatchEngine (Set/Add/Update/Resolve + audit MemoryChange)
                          ├── ConversationService (cap 30 msgs, H-013 metrics)
                          ├── ILLMProviderRouter (gemini-default → groq-qwen-32b fallback)
                          └── GeminiProvider (retry 429/503, backoff)
```

**Vertical slice ligero** (no Clean Architecture completa — MVP):
```
/src
  /LearningAgents.Api            → controllers, Program.cs, Swagger, PROMPT_GLOBAL.md copy
  /LearningAgents.Domain          → entidades (Tutor, StudySession, Message, MemoryEntry, MemoryChange), enums, MemoryKeys
  /LearningAgents.Infrastructure  → DbContext, Migrations, LLM providers
  /LearningAgents.Application     → TutorService, PromptBuilder, MemoryPatchEngine, ConversationService
/tests
  /LearningAgents.Tests           → 48 tests (MemoryPatchEngine, PromptBuilder, H-013 scaling)
```

**Flujo conversacional** `endpoints.md:132`:
`POST /api/sessions/{id}/messages` → persiste `Message` user → `PromptBuilder` (systemPrompt + memorias según profile) → `ILLMProviderRouter` → loop tool calling (≤5) → persiste `Message` assistant + `MemoryChange`.

**Decisiones clave:** `Guid→int` `decisiones.md:54`, `Service layer` `108`, `Multi-provider` `42`.
