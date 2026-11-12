#!/usr/bin/env bash
# Generate a fresh domain-neutral starter, compile, run xUnit and check actual HTTP.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
for command in dotnet python3 curl; do
  if ! command -v "$command" >/dev/null 2>&1; then
    echo "MISSING: $command is required for starter verification" >&2
    exit 3
  fi
done
python3 scripts/test_starter_template.py
TEMP="$(mktemp -d)"
SERVER_PID=""
cleanup() {
  if [[ -n "$SERVER_PID" ]]; then
    kill "$SERVER_PID" 2>/dev/null || true
    wait "$SERVER_PID" 2>/dev/null || true
  fi
  dotnet new uninstall "$ROOT/templates/aba-api" >/dev/null 2>&1 || true
  rm -rf "$TEMP"
}
trap cleanup EXIT
dotnet new install "$ROOT/templates/aba-api" >/dev/null
dotnet new aba-api -n Pilot.QuickStart -o "$TEMP/quickstart"
OUTPUT="$TEMP/quickstart"
SOLUTION="$OUTPUT/Pilot.QuickStart.slnx"
test -f "$SOLUTION"
test -f "$OUTPUT/AGENTS.md"
test -f "$OUTPUT/metadata/contexts.json"
if grep -R -E "namespace Academy|Academy\\." "$OUTPUT/src" "$OUTPUT/tests" --include='*.cs' >/dev/null; then
  echo "FAIL: sample business type leaked into generated starter" >&2
  exit 1
fi
# Exercise the new bounded-context generator only inside this ephemeral CI fixture.
python3 "$OUTPUT/tools/new_context.py" check
python3 "$OUTPUT/tools/new_context.py" create --name BoundaryProbe
python3 "$OUTPUT/tools/new_context.py" check
python3 "$OUTPUT/tools/new_context.py" create --name AuditProbe --with-db
python3 "$OUTPUT/tools/new_context.py" check
# Deliberately introduce an illegal cross-context source dependency and confirm
# executable governance rejects it, then restore the file unchanged.
DOMAIN="$OUTPUT/src/Modules/BoundaryProbe/Domain/AssemblyMarker.cs"
cp "$DOMAIN" "$TEMP/domain-backup.cs"
printf '\nusing Pilot.QuickStart.AuditProbe.Infrastructure;\n' >> "$DOMAIN"
if python3 "$OUTPUT/tools/new_context.py" check; then
  echo "FAIL: governance accepted a private cross-context import" >&2; exit 1
fi
cp "$TEMP/domain-backup.cs" "$DOMAIN"
# Same source file must not import EF Core into the pure Domain.
printf '\nusing Microsoft.EntityFrameworkCore;\n' >> "$DOMAIN"
if python3 "$OUTPUT/tools/new_context.py" check; then
  echo "FAIL: governance accepted an EF Core import in Domain" >&2; exit 1
fi
cp "$TEMP/domain-backup.cs" "$DOMAIN"
python3 "$OUTPUT/tools/new_context.py" check
if python3 "$OUTPUT/tools/new_context.py" create --name BoundaryProbe; then
  echo "FAIL: context generator accepted a duplicate" >&2; exit 1
fi
if python3 "$OUTPUT/tools/new_context.py" create --name '../Escape'; then
  echo "FAIL: context generator accepted a path-escape name" >&2; exit 1
fi
test -f "$OUTPUT/src/Modules/BoundaryProbe/_Docs/ownership.yaml"
test -f "$OUTPUT/tests/Pilot.QuickStart.BoundaryProbe.Tests/Pilot.QuickStart.BoundaryProbe.Tests.csproj"
dotnet restore "$SOLUTION"
dotnet build "$SOLUTION" -c Release --no-restore
dotnet test "$SOLUTION" -c Release --no-build
URL="http://127.0.0.1:5167"
DLL="$OUTPUT/src/Pilot.QuickStart.Api/bin/Release/net10.0/Pilot.QuickStart.Api.dll"
test -f "$DLL"
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="$URL" dotnet "$DLL" > "$TEMP/host.log" 2>&1 &
SERVER_PID=$!
ready=0
for ((i=0; i<80; i++)); do
  if curl -fsS --max-time 2 "$URL/health/live" > "$TEMP/health.json" 2>/dev/null; then
    ready=1; break
  fi
  if ! kill -0 "$SERVER_PID" 2>/dev/null; then break; fi
  sleep 0.25
done
if (( ready == 0 )); then
  cat "$TEMP/host.log" >&2
  echo "FAIL: generated HTTP host did not start" >&2
  exit 1
fi
curl -fsS "$URL/openapi/v1.json" > "$TEMP/openapi.json"
curl -fsSL "$URL/scalar" > "$TEMP/scalar.html"
python3 - "$TEMP/health.json" "$TEMP/openapi.json" "$TEMP/scalar.html" <<'PY'
import json, sys
from pathlib import Path
health = Path(sys.argv[1]).read_text().strip().lower()
spec = json.loads(Path(sys.argv[2]).read_text())
html = Path(sys.argv[3]).read_text()
assert health in ('healthy', 'live'), f'unexpected health response: {health!r}'
assert '/api/v1/system/status' in spec.get('paths', {}), 'generated API not in OpenAPI'
assert len(html) > 50, 'interactive docs missing'
print('PASS: generated HTTP starter health, dynamic OpenAPI and interactive Scalar route')
PY
echo "PASS: fresh named ABA API generated, built, tested and served successfully"
