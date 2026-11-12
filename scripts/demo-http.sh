#!/usr/bin/env bash
set -euo pipefail
URL="${URL:-http://127.0.0.1:5080}"
COURSE="dc1311ec-bbd1-4ed7-a18c-886ff51f41b3"
EMAIL="aba-demo-$(date +%s)-$RANDOM@example.org"
USER_JSON=$(curl -fsS -X POST "$URL/api/v1/users" -H 'Content-Type: application/json' -d "{\"email\":\"$EMAIL\"}")
USER_ID=$(printf '%s' "$USER_JSON" | python3 -c 'import sys,json; print(json.load(sys.stdin)["userId"])')
ENROLLMENT_JSON=$(curl -fsS -X POST "$URL/api/v1/enrollments" -H 'Content-Type: application/json' -d "{\"userId\":\"$USER_ID\",\"courseId\":\"$COURSE\"}")
ENROLLMENT_ID=$(printf '%s' "$ENROLLMENT_JSON" | python3 -c 'import sys,json; print(json.load(sys.stdin)["enrollmentId"])')
KEY="demo-$ENROLLMENT_ID"
printf 'User: %s\nEnrollment: %s\n' "$USER_ID" "$ENROLLMENT_ID"
curl -fsS -X POST "$URL/api/v1/payments/capture" -H 'Content-Type: application/json' -H "Idempotency-Key: $KEY" -d "{\"enrollmentId\":\"$ENROLLMENT_ID\"}"; echo
sleep 1
curl -fsS "$URL/api/v1/enrollments/$ENROLLMENT_ID"; echo
curl -fsS "$URL/api/v1/accounting/journals"; echo
curl -fsS "$URL/api/v1/reporting/summary"; echo
