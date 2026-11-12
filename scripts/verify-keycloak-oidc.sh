#!/usr/bin/env bash
# CI ONLY: actual OIDC discovery/JWKS and Keycloak-signed JWT against generated API.
# Test-only password grant is NEVER a production login recommendation.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
for command in dotnet docker curl python3; do
  command -v "$command" >/dev/null || { echo "MISSING $command" >&2; exit 3; }
done
TMP="$(mktemp -d)"
SERVER_PID=""
KEYCLOAK="aba-keycloak-ci-$$"
cleanup() {
  if [[ -n "$SERVER_PID" ]]; then
    kill "$SERVER_PID" 2>/dev/null || true
    wait "$SERVER_PID" 2>/dev/null || true
  fi
  docker rm -f "$KEYCLOAK" >/dev/null 2>&1 || true
  dotnet new uninstall "$ROOT/templates/aba-api" >/dev/null 2>&1 || true
  rm -rf "$TMP"
}
trap cleanup EXIT

# Registry 502/503 errors are transient and must not invalidate JWT behavior.
# Retry a pinned image; never silently switch to an unpinned latest version.
IMAGE="quay.io/keycloak/keycloak:26.4.0"
PULLED=0
for attempt in 1 2 3 4; do
  if docker pull "$IMAGE" > "$TMP/keycloak-pull.log" 2>&1; then
    PULLED=1; break
  fi
  echo "Keycloak image registry unavailable on attempt $attempt/4" >&2
  sleep $((attempt * 5))
done
if [[ "$PULLED" -ne 1 ]]; then
  tail -n 30 "$TMP/keycloak-pull.log" >&2
  echo "FAIL: pinned Keycloak image could not be fetched" >&2
  exit 1
fi

docker run -d --rm --name "$KEYCLOAK" -m 1536m \
  -p 127.0.0.1:8180:8080 \
  -e KC_BOOTSTRAP_ADMIN_USERNAME=admin \
  -e KC_BOOTSTRAP_ADMIN_PASSWORD=Aba_CiAdmin_2026! \
  -e KC_HOSTNAME=http://localhost:8180 \
  -v "$ROOT/infra/keycloak/aba-ci-realm.json:/opt/keycloak/data/import/aba-ci-realm.json:ro" \
  "$IMAGE" start-dev --import-realm >/dev/null

IDP="http://localhost:8180/realms/aba-ci"
READY=0
for ((i=0; i<180; i++)); do
  if curl -fsS --max-time 3 "$IDP/.well-known/openid-configuration" > "$TMP/openid.json" 2>/dev/null; then
    READY=1; break
  fi
  sleep 1
done
if [[ "$READY" -ne 1 ]]; then
  docker logs "$KEYCLOAK" >&2 || true
  echo "FAIL: Keycloak OIDC discovery was not available" >&2
  exit 1
fi
python3 - "$TMP/openid.json" "$IDP" <<'PY'
import json, sys
config = json.load(open(sys.argv[1]))
assert config["issuer"] == sys.argv[2], config.get("issuer")
assert config["jwks_uri"].startswith(sys.argv[2])
print("PASS: real Keycloak discovery with issuer and JWKS")
PY

dotnet new install "$ROOT/templates/aba-api" >/dev/null
dotnet new aba-api -n Pilot.Keycloak -o "$TMP/api" >/dev/null
dotnet build "$TMP/api/Pilot.Keycloak.slnx" -c Release >/dev/null
ASPNETCORE_ENVIRONMENT=Development \
ASPNETCORE_URLS=http://127.0.0.1:5181 \
Authentication__Authority="$IDP" \
Authentication__Audience=aba-api-ci \
Authentication__AllowHttpMetadataInDevelopment=true \
Platform__Tenancy__Enabled=true \
dotnet "$TMP/api/src/Pilot.Keycloak.Api/bin/Release/net10.0/Pilot.Keycloak.Api.dll" \
  > "$TMP/api.log" 2>&1 &
SERVER_PID="$!"

APIREADY=0
for ((i=0; i<80; i++)); do
  if curl -fsS --max-time 2 http://127.0.0.1:5181/health/live >/dev/null 2>&1; then
    APIREADY=1; break
  fi
  if ! kill -0 "$SERVER_PID" 2>/dev/null; then
    cat "$TMP/api.log" >&2
    echo "FAIL: generated API stopped before OIDC test" >&2
    exit 1
  fi
  sleep 0.5
done
if [[ "$APIREADY" -ne 1 ]]; then
  cat "$TMP/api.log" >&2
  echo "FAIL: generated API health endpoint unavailable" >&2
  exit 1
fi

get_token() {
  local client="$1" payload="$TMP/token-$1.json"
  local status
  status="$(curl -sS --max-time 20 -o "$payload" -w '%{http_code}' \
    -X POST "$IDP/protocol/openid-connect/token" \
    -d 'grant_type=password' \
    -d 'username=aba-ci-user' -d 'password=Aba_CiUser_2026!' \
    --data-urlencode "client_id=$client")"
  if [[ "$status" != "200" ]]; then
    python3 - "$payload" "$client" "$status" <<'PY'
import json,sys
try:
    response=json.load(open(sys.argv[1]))
    print(f"FAIL: Keycloak test client {sys.argv[2]} HTTP {sys.argv[3]}: "
          f"{response.get('error', 'unknown')} / {response.get('error_description', '')}",
          file=sys.stderr)
except Exception:
    print(f"FAIL: Keycloak token HTTP {sys.argv[3]} for {sys.argv[2]}", file=sys.stderr)
PY
    return 1
  fi
  python3 - "$payload" <<'PY'
import json, sys
with open(sys.argv[1]) as stream:
    value = json.load(stream)
assert value.get("access_token"), "Keycloak did not issue access_token"
print(value["access_token"])
PY
}
assert_status() {
  local expected="$1" token="$2" actual=""
  if [[ -n "$token" ]]; then
    actual="$(curl -sS --max-time 15 -o /dev/null -w '%{http_code}' \
      -H "Authorization: Bearer $token" \
      http://127.0.0.1:5181/api/v1/system/authorization-check)"
  else
    actual="$(curl -sS --max-time 15 -o /dev/null -w '%{http_code}' \
      http://127.0.0.1:5181/api/v1/system/authorization-check)"
  fi
  if [[ "$actual" != "$expected" ]]; then
    echo "FAIL: expected HTTP $expected, got $actual" >&2
    tail -n 60 "$TMP/api.log" >&2
    exit 1
  fi
}
GOOD="$(get_token aba-api-ci)"
NO_TENANT="$(get_token aba-no-tenant)"
WRONG_AUDIENCE="$(get_token aba-wrong-audience)"
assert_status 401 ""
assert_status 204 "$GOOD"
assert_status 403 "$NO_TENANT"
assert_status 401 "$WRONG_AUDIENCE"
assert_status 401 "tampered.invalid.signature"
echo "PASS: actual Keycloak OIDC/JWKS and JWT 204/401/403 tenant security cases"
