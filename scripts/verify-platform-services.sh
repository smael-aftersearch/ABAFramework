#!/usr/bin/env bash
# Run real PostgreSQL, SQL Server and Redis tests from a newly generated API.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
for variable in ABA_TEST_POSTGRESQL ABA_TEST_SQLSERVER ABA_TEST_REDIS; do
  if [[ -z "${!variable:-}" ]]; then
    echo "MISSING $variable: real infrastructure tests require all three connection strings" >&2
    exit 3
  fi
done
if ! command -v dotnet >/dev/null 2>&1; then
  echo "MISSING: dotnet 10 SDK" >&2
  exit 3
fi
DIR="$(mktemp -d)"
cleanup() {
  dotnet new uninstall "$ROOT/templates/aba-api" >/dev/null 2>&1 || true
  rm -rf "$DIR"
}
trap cleanup EXIT
dotnet new install "$ROOT/templates/aba-api" >/dev/null
dotnet new aba-api -n Pilot.Backends -o "$DIR/generic"
PROJECT="$DIR/generic/Pilot.Backends.slnx"
dotnet restore "$PROJECT"
dotnet build "$PROJECT" -c Release --no-restore
dotnet test "$PROJECT" -c Release --no-build \
  --filter 'FullyQualifiedName~AbaExternalInfrastructureTests' \
  --logger 'console;verbosity=normal'
echo "PASS: PostgreSQL, SQL Server and Redis integration suite on generated project"
