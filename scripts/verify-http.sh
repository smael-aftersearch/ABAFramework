#!/usr/bin/env bash
# Prove the DEVELOPMENT-ONLY HTTP host launches and the Academy API demo completes.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
for required in dotnet curl python3; do
  if ! command -v "$required" >/dev/null 2>&1; then
    echo "Missing required command: $required" >&2
    exit 3
  fi
done
PORT="${ACADEMY_HTTP_TEST_PORT:-5080}"
if [[ ! "$PORT" =~ ^[0-9]+$ ]]; then
  echo "ACADEMY_HTTP_TEST_PORT must contain digits" >&2
  exit 2
fi
URL="http://127.0.0.1:$PORT"
HOST_DLL="samples/Academy/Host/bin/Release/net10.0/Academy.Host.dll"
if [[ ! -f "$HOST_DLL" ]]; then
  echo "Missing Release output: run dotnet build ABA.Net10.Phase1.slnx -c Release first" >&2
  exit 3
fi
TMPDIR_TEST="$(mktemp -d)"
PID=""
cleanup() {
  if [[ -n "$PID" ]]; then
    kill "$PID" 2>/dev/null || true
    wait "$PID" 2>/dev/null || true
  fi
  rm -rf "$TMPDIR_TEST"
}
trap cleanup EXIT
export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS="$URL"
export ACADEMY_DATA_DIR="$TMPDIR_TEST/data"
dotnet "$HOST_DLL" > "$TMPDIR_TEST/server.log" 2>&1 &
PID=$!
ready=0
for ((i=0; i<80; i++)); do
  if curl -fsS --max-time 2 "$URL/health/live" > "$TMPDIR_TEST/health.json" 2>/dev/null; then
    ready=1
    break
  fi
  if ! kill -0 "$PID" 2>/dev/null; then
    break
  fi
  sleep 0.25
done
if (( ready == 0 )); then
  echo "Academy HTTP host failed to become ready" >&2
  cat "$TMPDIR_TEST/server.log" >&2
  exit 1
fi
curl -fsS "$URL/openapi/v1.json" > "$TMPDIR_TEST/openapi.json"
python3 - "$TMPDIR_TEST/health.json" "$TMPDIR_TEST/openapi.json" <<'PY'
import json, sys
with open(sys.argv[1], encoding="utf-8") as f:
    health = json.load(f)
assert health.get("status") == "live", "Health status must be live"
with open(sys.argv[2], encoding="utf-8") as f:
    spec = json.load(f)
assert "openapi" in spec, "OpenAPI document version is missing"
assert "/api/v1/users" in spec.get("paths", {}), "User API not in OpenAPI document"
print("PASS: health and OpenAPI response validation")
PY
if ! URL="$URL" bash scripts/demo-http.sh > "$TMPDIR_TEST/demo.log" 2>&1; then
  echo "Academy HTTP paid-enrollment demo failed:" >&2
  cat "$TMPDIR_TEST/demo.log" >&2
  cat "$TMPDIR_TEST/server.log" >&2
  exit 1
fi
echo "PASS: loopback HTTP host, OpenAPI, Academy API demo"
