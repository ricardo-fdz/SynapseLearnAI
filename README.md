# Synapse Learn AI

Plataforma de aprendizaje asistido por IA con tutores configurables, sesiones de estudio, memoria persistente, auditoría y chat contextual.

## Estructura

```text
frontend/  Angular 21 + Tailwind CSS
backend/   .NET 10 API + SQLite
```

Cada proyecto conserva su documentación técnica en su propia carpeta `docs/`.

## Requisitos

- Node.js con npm 10+
- .NET SDK 10
- Credenciales de los proveedores LLM configuradas mediante User Secrets o variables de entorno en el backend

## Desarrollo local

En una terminal, inicia la API:

```bash
cd backend
dotnet run --project src/LearningAgents.Api
```

La API se expone en `http://localhost:5017`.

En otra terminal, inicia el frontend:

```bash
cd frontend
npm ci
npm start
```

Abre `http://localhost:4200`.

## Verificación

```bash
cd frontend && npm run build
cd backend && dotnet build LearningAgents.slnx
```

## Seguridad

No subas credenciales, archivos `.env`, bases SQLite locales ni configuraciones locales. Usa User Secrets o variables de entorno para las claves de Gemini, Groq u otros proveedores.
