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
INTEGRATION="$DIR/generic/tests/Pilot.Backends.Platform.IntegrationTests/Pilot.Backends.Platform.IntegrationTests.csproj"
dotnet restore "$PROJECT"
dotnet build "$PROJECT" -c Release --no-restore
dotnet restore "$INTEGRATION"
dotnet build "$INTEGRATION" -c Release --no-restore
# Keep normal developer integration tests unchanged; the process drill is
# explicitly opted in and independently refuses non-CI / off-host execution.
if [[ "${ABA_ENABLE_SHARED_RATE_LIMIT_PROCESS_DRILL:-}" == "yes" ]]; then
  python3 "$ROOT/scripts/verify_shared_rate_limit_processes.py" \
    --dll "$DIR/generic/src/Pilot.Backends.Api/bin/Release/net10.0/Pilot.Backends.Api.dll"
fi
dotnet test "$INTEGRATION" -c Release --no-build --filter 'Category!=RecoveryDrill&Category!=CrashDrill&Category!=RedisRestartDrill' --logger 'console;verbosity=normal'
if [[ "${ABA_ENABLE_DESTRUCTIVE_CI_DRILLS:-}" == "yes" ]]; then
  # Both providers receive disposable, uniquely named databases. Never run
  # against production targets; the C# fixture independently enforces CI-only
  # localhost/database safety.
  dotnet test "$INTEGRATION" -c Release --no-build --filter 'Category=RecoveryDrill' --logger 'console;verbosity=normal'
  # Actual child-process SIGKILL probes: pre-commit rollback and durability
  # after a scheduled envelope has been persisted on the disposable CI DB.
  dotnet test "$INTEGRATION" -c Release --no-build --filter 'Category=CrashDrill' --logger 'console;verbosity=normal'
fi
echo "PASS: PostgreSQL, SQL Server, Redis and Wolverine message probes on generated project"
