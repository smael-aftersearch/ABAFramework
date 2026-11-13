#!/usr/bin/env bash
# Produce a versioned .NET template NuGet package and verify real installation.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
command -v dotnet >/dev/null 2>&1 || { echo "Missing dotnet 10 SDK" >&2; exit 3; }
TMP="$(mktemp -d)"
PACKAGE=""
PACKAGE_ID="ABAFramework.GenericApi.Template"
TEMPLATE_INSTALLED=0
cleanup() {
  if [[ "$TEMPLATE_INSTALLED" -eq 1 ]]; then
    dotnet new uninstall "$PACKAGE_ID" >/dev/null 2>&1 || true
  fi
  rm -rf "$TMP" "$ROOT/packaging/content/aba-api"
}
trap cleanup EXIT
# NuGet packaging needs the template inside the packaging project root.
# Stage it temporarily so ALL source files, including hidden AI instructions,
# enter the .nupkg rather than only the .template.config metadata.
mkdir -p "$ROOT/packaging/content/aba-api" artifacts/packages
cp -a "$ROOT/templates/aba-api/." "$ROOT/packaging/content/aba-api/"
dotnet pack packaging/ABAFramework.GenericApi.Template.csproj -c Release \
  -o "$ROOT/artifacts/packages" \
  -p:PackageVersion=0.3.0-preview.2
PACKAGE="$ROOT/artifacts/packages/ABAFramework.GenericApi.Template.0.3.0-preview.2.nupkg"
test -s "$PACKAGE"
python3 - "$PACKAGE" <<'PY'
import sys, zipfile
with zipfile.ZipFile(sys.argv[1]) as z:
    names = z.namelist()
    print("Package inventory:", len(names), "entries")
    for name in names[:100]:
        print("  ZIP:", name)
    for required in ("AGENTS.md", "Dockerfile", ".agents/skills/aba-engineering/SKILL.md"):
        matched = [name for name in names if name.endswith("/" + required)]
        print("Package lookup:", required, matched[:4])
PY
if dotnet new uninstall 2>/dev/null | grep -Fq "$PACKAGE_ID"; then
  echo "BLOCKED: $PACKAGE_ID is already installed; remove it before running this isolated package check" >&2
  exit 3
fi
TEMPLATE_INSTALLED=1
dotnet new install "$PACKAGE"
dotnet new aba-api -n Pilot.Packaged -o "$TMP/from-package"
for required in \
  AGENTS.md \
  .agents/skills/aba-engineering/SKILL.md \
  Dockerfile \
  .github/workflows/ci.yml \
  .github/workflows/integration.yml \
  tools/supply_chain.py \
  tools/test_supply_chain.py \
  tools/secret_scan.py \
  tools/test_secret_scan.py \
  .gitleaks.toml \
  .gitleaksignore \
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
