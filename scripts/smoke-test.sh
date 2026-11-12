#!/usr/bin/env bash
# Minimal post-deploy check against a revision URL: liveness, readiness (dependencies reachable),
# and that GraphQL refuses anonymous callers (auth is wired). No PHI is touched.
set -euo pipefail
BASE="$1"
curl -fsS --retry 5 --retry-delay 5 "$BASE/health/live" >/dev/null
curl -fsS --retry 5 --retry-delay 5 "$BASE/health/ready" >/dev/null
code=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$BASE/graphql" -H 'Content-Type: application/json' -d '{"query":"{ __typename }"}')
[[ "$code" == "401" ]] || { echo "Expected 401 from anonymous GraphQL, got $code"; exit 1; }
echo "Smoke test passed for $BASE"
