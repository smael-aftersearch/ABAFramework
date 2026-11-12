#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
# The fixed QA runner executes scripts/ai_check.py, scripts/test_ai_check.py,
# scripts/test_task_protocol.py, scripts/governance.py, scripts/test_governance.py,
# scripts/static_review.py, then .NET restore/build/console smoke when available.
python3 scripts/task_protocol.py verify --profile all \
  --output artifacts/verification/local.json \
  --markdown artifacts/verification/local.md
