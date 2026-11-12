#!/usr/bin/env bash
# Prove a fresh template produces a non-root working OCI image and fails closed in Production.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
for binary in docker dotnet curl python3 timeout; do
  if ! command -v "$binary" >/dev/null 2>&1; then
    echo "MISSING $binary for container test" >&2
    exit 3
  fi
done
TEMP="$(mktemp -d)"
IMAGE="aba-ci-generic:local"
CID=""
cleanup() {
  if [[ -n "$CID" ]]; then docker rm -f "$CID" >/dev/null 2>&1 || true; fi
  docker image rm "$IMAGE" >/dev/null 2>&1 || true
  dotnet new uninstall "$ROOT/templates/aba-api" >/dev/null 2>&1 || true
  rm -rf "$TEMP"
}
trap cleanup EXIT
dotnet new install "$ROOT/templates/aba-api" >/dev/null
dotnet new aba-api -n Pilot.Container -o "$TEMP/container" >/dev/null
docker build -t "$IMAGE" "$TEMP/container"
# A production host with no trusted OIDC settings MUST exit, not listen.
set +e
timeout 15s docker run --rm --network none -e ASPNETCORE_ENVIRONMENT=Production "$IMAGE" \
  > "$TEMP/prod.log" 2>&1
EXIT_CODE=$?
set -e
if [[ "$EXIT_CODE" -eq 0 || "$EXIT_CODE" -eq 124 ]]; then
  cat "$TEMP/prod.log" >&2
  echo "FAIL: container did not reject unauthenticated production startup" >&2
  exit 1
fi
grep -q 'Non-development API requires' "$TEMP/prod.log"
CID="$(docker run -d --rm -p 127.0.0.1:5168:8080 \
    -e ASPNETCORE_ENVIRONMENT=Development "$IMAGE")"
READY=0
for ((i=0; i<80; i++)); do
  if curl -fsS --max-time 2 http://127.0.0.1:5168/health/live > "$TEMP/health" 2>/dev/null; then
    READY=1; break
  fi
  if [[ "$(docker inspect -f '{{.State.Running}}' "$CID" 2>/dev/null || true)" != "true" ]]; then
    break
  fi
  sleep 0.25
done
if [[ "$READY" -ne 1 ]]; then
  docker logs "$CID" >&2 || true
  echo "FAIL: generated OCI container never became healthy" >&2
  exit 1
fi
curl -fsS http://127.0.0.1:5168/openapi/v1.json > "$TEMP/openapi.json"
curl -fsSL http://127.0.0.1:5168/scalar > "$TEMP/scalar.html"
python3 - "$TEMP/health" "$TEMP/openapi.json" "$TEMP/scalar.html" <<'PY'
import json, pathlib, sys
health = pathlib.Path(sys.argv[1]).read_text().strip().lower()
spec = json.loads(pathlib.Path(sys.argv[2]).read_text())
html = pathlib.Path(sys.argv[3]).read_text()
assert health == 'healthy', health
assert '/api/v1/system/authorization-check' in spec['paths']
assert len(html) > 50
print('PASS: generated non-root OCI image serves local Health/OpenAPI/Scalar')
PY
UID_VALUE="$(docker exec "$CID" id -u)"
if [[ "$UID_VALUE" == "0" ]]; then
  echo "FAIL: generated container is running as root" >&2
  exit 1
fi
echo "PASS: OCI image rejects unconfigured Production and serves isolated Development HTTP"
