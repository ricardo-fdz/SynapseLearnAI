.PHONY: dev dev-backend dev-frontend build test install clean

dev: ## Inicia backend + frontend (requiere concurrently: npm install)
	npm run dev

dev-sh: ## Inicia backend + frontend sin dependencias extra (bash)
	./dev.sh

dev-backend:
	dotnet run --project backend/src/LearningAgents.Api

dev-frontend:
	npm start --prefix frontend

build:
	dotnet build backend/LearningAgents.slnx -c Release
	npm run build --prefix frontend

test:
	dotnet test backend
	npm test --prefix frontend -- --run

install:
	dotnet restore backend/LearningAgents.slnx
	npm ci --prefix frontend
	npm install

clean:
	dotnet clean backend/LearningAgents.slnx
	rm -rf frontend/dist frontend/.angular

help:
	@grep -E '^[a-z-]+:.*?##' Makefile | awk 'BEGIN{FS=":.*?##"} {printf "  \033[36m%-15s\033[0m %s\n", $$1, $$2}'
