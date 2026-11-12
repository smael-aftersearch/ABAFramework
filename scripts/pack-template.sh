#!/usr/bin/env bash
# Produce a versioned .NET template NuGet package and verify real installation.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
command -v dotnet >/dev/null 2>&1 || { echo "Missing dotnet 10 SDK" >&2; exit 3; }
TMP="$(mktemp -d)"
PACKAGE=""
cleanup() {
  if [[ -n "$PACKAGE" ]]; then dotnet new uninstall "$PACKAGE" >/dev/null 2>&1 || true; fi
  rm -rf "$TMP"
}
trap cleanup EXIT
mkdir -p artifacts/packages
dotnet pack packaging/ABAFramework.GenericApi.Template.csproj -c Release \
  -o "$ROOT/artifacts/packages" \
  /p:PackageVersion=0.2.0-preview.1
PACKAGE="$ROOT/artifacts/packages/ABAFramework.GenericApi.Template.0.2.0-preview.1.nupkg"
test -s "$PACKAGE"
dotnet new install "$PACKAGE"
dotnet new aba-api -n Pilot.Packaged -o "$TMP/from-package"
for required in \
  AGENTS.md \
  .agents/skills/aba-engineering/SKILL.md \
  Dockerfile \
  tests/Pilot.Packaged.Platform.IntegrationTests/Pilot.Packaged.Platform.IntegrationTests.csproj; do
  if [[ ! -f "$TMP/from-package/$required" ]]; then
    echo "FAIL: template package omitted generated file: $required" >&2
    find "$TMP/from-package" -maxdepth 5 -type f | sort | head -n 90 >&2
    exit 1
  fi
done
dotnet build "$TMP/from-package/Pilot.Packaged.slnx" -c Release
dotnet test "$TMP/from-package/Pilot.Packaged.slnx" -c Release --no-build
echo "PASS: versioned ABA NuGet template package installs and generates a tested new API"
