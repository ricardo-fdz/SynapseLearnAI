#!/usr/bin/env bash
# dev.sh — inicia backend y frontend en paralelo sin dependencias extra
# Uso: ./dev.sh  (Ctrl+C detiene ambos)
set -e
ROOT="$(cd "$(dirname "$0")" && pwd)"
BACKEND_PID=""
FRONTEND_PID=""

cleanup() {
  echo ""
  echo "Deteniendo servicios..."
  [ -n "$FRONTEND_PID" ] && kill $FRONTEND_PID 2>/dev/null || true
  [ -n "$BACKEND_PID" ] && kill $BACKEND_PID 2>/dev/null || true
  wait 2>/dev/null || true
  echo "Listo."
}
trap cleanup INT TERM EXIT

echo "→ Backend  http://localhost:5017 (dotnet run)"
dotnet run --project "$ROOT/backend/src/LearningAgents.Api" &
BACKEND_PID=$!

echo "→ Frontend http://localhost:4200 (ng serve)"
# espera a que backend compile antes de arrancar frontend (evita log entrelazado inicial)
sleep 2
npm start --prefix "$ROOT/frontend" &
FRONTEND_PID=$!

echo ""
echo "Ambos servicios corriendo. Logs entrelazados abajo. Ctrl+C para salir."
echo "  Backend PID $BACKEND_PID | Frontend PID $FRONTEND_PID"
echo ""

wait
