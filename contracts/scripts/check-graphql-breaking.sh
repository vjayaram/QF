#!/usr/bin/env bash
# GraphQL contract gate. Run after scripts/compose-gateway.ps1, which regenerates
# contracts/graphql/composed-schema.graphql (the public schema the Fusion gateway serves).
#
#   1. Snapshot drift: the regenerated schema must equal the committed snapshot, so every schema
#      change is visible in the pull request diff.
#   2. Breaking changes: compare main's snapshot with this change using GraphQL Inspector.
#      Removing a field that main already marked @deprecated is allowed (deprecate first, remove later).
#
# Environment:
#   SYSTEM_ACCESSTOKEN               Azure DevOps job token, used to fetch main (CI only).
#   ALLOW_BREAKING_GRAPHQL_CHANGES   "true" downgrades breaking changes to a warning. Set it at queue time,
#                                    only after consumers have agreed to the break.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$repo_root"
snapshot="contracts/graphql/composed-schema.graphql"
inspector="contracts/node_modules/.bin/graphql-inspector"
in_ci="${TF_BUILD:-}"

log_error() { if [[ -n "$in_ci" ]]; then echo "##vso[task.logissue type=error]$1"; else echo "ERROR: $1" >&2; fi; }
log_warning() { if [[ -n "$in_ci" ]]; then echo "##vso[task.logissue type=warning]$1"; else echo "WARNING: $1" >&2; fi; }

if [[ ! -f "$snapshot" ]]; then
  log_error "$snapshot was not generated. Run scripts/compose-gateway.ps1 first."
  exit 1
fi
if [[ ! -x "$inspector" ]]; then
  log_error "GraphQL Inspector is not installed. Run: npm ci --prefix contracts"
  exit 1
fi

# 1. Snapshot drift (also catches a snapshot that was never committed).
if [[ -n "$(git status --porcelain -- "$snapshot")" ]]; then
  git --no-pager diff -- "$snapshot" || true
  log_error "The GraphQL schema changed but $snapshot was not updated. Run scripts/compose-gateway.ps1 and commit the snapshot."
  exit 1
fi
echo "Snapshot matches the composed schema."

# 2. Breaking changes against main.
if [[ -n "${SYSTEM_ACCESSTOKEN:-}" ]]; then
  git -c http.extraheader="AUTHORIZATION: bearer ${SYSTEM_ACCESSTOKEN}" fetch --quiet --depth=1 origin main
else
  git fetch --quiet --depth=1 origin main
fi

baseline="$(mktemp --suffix=.graphql)"
trap 'rm -f "$baseline"' EXIT
if ! git show "FETCH_HEAD:$snapshot" > "$baseline" 2>/dev/null; then
  log_warning "main has no $snapshot yet; skipping the breaking-change check until the first snapshot is merged."
  exit 0
fi

set +e
"$inspector" diff "$baseline" "$snapshot" --rule suppressRemovalOfDeprecatedField
status=$?
set -e

if [[ $status -ne 0 ]]; then
  if [[ "${ALLOW_BREAKING_GRAPHQL_CHANGES:-false}" == "true" ]]; then
    log_warning "Breaking GraphQL changes allowed for this run (ALLOW_BREAKING_GRAPHQL_CHANGES=true)."
    exit 0
  fi
  log_error "Breaking GraphQL changes against main. Deprecate the field first (@deprecated) and remove it in a later release, or get consumer sign-off and queue the build with ALLOW_BREAKING_GRAPHQL_CHANGES=true."
  exit 1
fi
echo "No breaking GraphQL changes against main."
