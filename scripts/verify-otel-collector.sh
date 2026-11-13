#!/usr/bin/env bash
# CI-only genuine OTLP/gRPC end-to-end: generated API -> local Collector -> debug exporter.
# No persistent telemetry storage, off-host bind, credentials or production target.
set -euo pipefail
if [[ "${CI:-}" != "true" || "${ABA_ENABLE_OTEL_COLLECTOR_DRILL:-}" != "yes" ]]; then
  echo "BLOCKED: collector drill needs explicit CI + opt-in" >&2
  exit 3
fi
for command in dotnet docker curl python3; do
  command -v "$command" >/dev/null || { echo "MISSING: $command" >&2; exit 3; }
done
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
TMP="$(mktemp -d)"
CONTAINER="aba-otel-collector-ci-$$"
SERVER_PID=""
cleanup() {
  if [[ -n "$SERVER_PID" ]]; then
    kill "$SERVER_PID" 2>/dev/null || true
    wait "$SERVER_PID" 2>/dev/null || true
  fi
  docker rm -f "$CONTAINER" >/dev/null 2>&1 || true
  dotnet new uninstall "$ROOT/templates/aba-api" >/dev/null 2>&1 || true
  rm -rf "$TMP"
}
trap cleanup EXIT

# Version is explicit: never pull an arbitrary latest Collector in the release gate.
IMAGE="otel/opentelemetry-collector:0.165.0"
docker pull "$IMAGE" >"$TMP/collector-pull.log" 2>&1 || {
  tail -n 30 "$TMP/collector-pull.log" >&2
  echo "FAIL: versioned Collector image unavailable" >&2
  exit 1
}
docker run -d --rm --name "$CONTAINER" \
  -p 127.0.0.1:4317:4317 \
  -v "$ROOT/infra/otel-collector-ci.yaml:/etc/otelcol/config.yaml:ro" \
  "$IMAGE" --config=/etc/otelcol/config.yaml >/dev/null

READY=0
for ((i=0; i<90; i++)); do
  if [[ "$(docker inspect -f '{{.State.Running}}' "$CONTAINER" 2>/dev/null || true)" != "true" ]]; then
    docker logs "$CONTAINER" >&2 || true
    echo "FAIL: collector exited" >&2
    exit 1
  fi
  if python3 -c 'import socket; s=socket.create_connection(("127.0.0.1",4317),1); s.close()' 2>/dev/null; then
    READY=1
    break
  fi
  sleep 0.5
done
[[ "$READY" == "1" ]] || { echo "FAIL: OTLP receiver unavailable" >&2; exit 1; }

# The only published receiver port MUST remain loopback-only on the runner.
BIND="$(docker port "$CONTAINER" 4317/tcp)"
[[ "$BIND" == "127.0.0.1:4317" ]] || {
  echo "FAIL: OTLP receiver not limited to loopback: $BIND" >&2
  exit 1
}

dotnet new install "$ROOT/templates/aba-api" >/dev/null
dotnet new aba-api -n Pilot.Collector -o "$TMP/generated" >/dev/null
dotnet build "$TMP/generated/Pilot.Collector.slnx" -c Release >/dev/null
ASPNETCORE_ENVIRONMENT=Development \
ASPNETCORE_URLS=http://127.0.0.1:5193 \
Platform__Observability__Enabled=true \
Platform__Observability__ServiceName=Pilot.Collector \
Platform__Observability__OtlpEndpoint=http://127.0.0.1:4317 \
OTEL_EXPORTER_OTLP_PROTOCOL=grpc \
OTEL_BSP_SCHEDULE_DELAY=500 \
OTEL_METRIC_EXPORT_INTERVAL=1000 \
dotnet "$TMP/generated/src/Pilot.Collector.Api/bin/Release/net10.0/Pilot.Collector.Api.dll" \
  >"$TMP/api.log" 2>&1 &
SERVER_PID="$!"

API_READY=0
for ((i=0; i<100; i++)); do
  if curl -fsS --max-time 2 http://127.0.0.1:5193/health/live >/dev/null 2>&1; then
    API_READY=1
    break
  fi
  if ! kill -0 "$SERVER_PID" 2>/dev/null; then
    tail -n 60 "$TMP/api.log" >&2
    echo "FAIL: generated API stopped before OTLP export" >&2
    exit 1
  fi
  sleep 0.5
done
[[ "$API_READY" == "1" ]] || {
  tail -n 60 "$TMP/api.log" >&2
  echo "FAIL: generated API unavailable" >&2
  exit 1
}

# Bounded, opt-in CI-only liveness baseline on this disposable generated API.
# This measures a single loopback process, never a production SLO or multi-node load.
ABA_ENABLE_LOOPBACK_LOAD_DRILL=yes python3 scripts/ci_loopback_load.py \
  --url http://127.0.0.1:5193/health/live

# A synthetic sensitive header is an exposure sentinel, NEVER a real credential.
SENTINEL="ABA_CI_SENTINEL_NOT_A_REAL_CREDENTIAL"
for ((i=0; i<6; i++)); do
  curl -fsS --max-time 3 -H "Authorization: Bearer $SENTINEL" \
    -H "X-Tenant-Id: $SENTINEL" http://127.0.0.1:5193/health/live >/dev/null
  curl -fsS --max-time 3 http://127.0.0.1:5193/api/v1/system/status >/dev/null
done

OBSERVED=0
LOG=""
for ((i=0; i<75; i++)); do
  LOG="$(docker logs "$CONTAINER" 2>&1)"
  if grep -Fq "ResourceSpans" <<<"$LOG" \
    && grep -Fq "ResourceMetrics" <<<"$LOG" \
    && grep -Fq "Pilot.Collector" <<<"$LOG" \
    && grep -Fq "http.server.request.duration" <<<"$LOG"; then
    OBSERVED=1
    break
  fi
  sleep 1
done
if [[ "$OBSERVED" != "1" ]]; then
  tail -n 100 "$TMP/api.log" >&2
  tail -n 110 <<<"$LOG" >&2
  echo "FAIL: Collector did not observe attributed HTTP spans and metrics" >&2
  exit 1
fi
if grep -Fq "$SENTINEL" <<<"$LOG"; then
  echo "FAIL: synthetic sensitive header leaked into Collector debug output" >&2
  exit 1
fi
echo "PASS: real Collector received generated API spans + HTTP metrics with correct service name, loopback-only and no synthetic header leak"
