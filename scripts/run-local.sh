#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
if ! command -v dotnet >/dev/null 2>&1; then
  echo "Missing .NET 10 SDK. Install .NET 10, then retry." >&2
  exit 3
fi
PORT="${ACADEMY_PORT:-5080}"
if [[ ! "$PORT" =~ ^[0-9]+$ ]]; then
  echo "ACADEMY_PORT must be a valid port number" >&2
  exit 2
fi
export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS="http://127.0.0.1:$PORT"
echo "ABA Academy local development API: $ASPNETCORE_URLS"
echo "Health: $ASPNETCORE_URLS/health/live"
echo "OpenAPI: $ASPNETCORE_URLS/openapi/v1.json"
echo "Warning: unauthenticated development demo; localhost only, never expose publicly."
exec dotnet run --project samples/Academy/Host --no-launch-profile
