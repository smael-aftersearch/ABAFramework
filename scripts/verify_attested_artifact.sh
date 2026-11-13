#!/usr/bin/env bash
# Shared, fail-closed post-signing verification for the tested NuGet and OCI artifacts.
set -euo pipefail

if [[ $# -ne 2 ]]; then
  echo 'usage: verify_attested_artifact.sh <artifact> <verification-json>' >&2
  exit 2
fi
: "${GH_TOKEN:?GH_TOKEN is required for signed provenance verification}"
: "${GITHUB_REPOSITORY:?GITHUB_REPOSITORY is required}"
: "${GITHUB_SHA:?GITHUB_SHA is required}"
if [[ ! "$GITHUB_SHA" =~ ^[0-9a-f]{40}$ ]]; then
  echo 'FAIL: invalid GitHub source commit SHA' >&2
  exit 2
fi

artifact="$1"
output="$2"
if [[ ! -f "$artifact" || ! -s "$artifact" ]]; then
  echo 'FAIL: signed artifact missing or empty' >&2
  exit 2
fi
mkdir -p "$(dirname "$output")"

gh attestation verify "$artifact" --repo "$GITHUB_REPOSITORY" \
  --source-ref refs/heads/main --source-digest "$GITHUB_SHA" \
  --format json > "$output"
test -s "$output"

scratch="$(mktemp -d)"
trap 'rm -rf "$scratch"' EXIT
mutated="$scratch/$(basename "$artifact")"
cp --reflink=auto "$artifact" "$mutated"
printf 'altered-after-scan' >> "$mutated"
if gh attestation verify "$mutated" --repo "$GITHUB_REPOSITORY" \
  --source-digest "$GITHUB_SHA" >/dev/null 2>&1; then
  echo 'FAIL: modified signed artifact was accepted' >&2
  exit 1
fi

# An authentic payload must not verify as a release from a different source SHA.
wrong_sha='0000000000000000000000000000000000000000'
if [[ "$GITHUB_SHA" == "$wrong_sha" ]]; then
  echo 'FAIL: invalid all-zero source commit' >&2
  exit 2
fi
if gh attestation verify "$artifact" --repo "$GITHUB_REPOSITORY" \
  --source-ref refs/heads/main --source-digest "$wrong_sha" >/dev/null 2>&1; then
  echo 'FAIL: wrong source commit accepted for signed artifact' >&2
  exit 1
fi

echo 'PASS: signed artifact, source identity and mutation denial verified'
