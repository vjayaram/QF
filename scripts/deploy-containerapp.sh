#!/usr/bin/env bash
# Progressive rollout of one Container App to a new image using revisions.
#   deploy-containerapp.sh <resource-group> <app-name> <image> <revision-suffix> <canary-percent>
# canary-percent = 100 -> blue/green (smoke-test the new revision, then switch all traffic).
# canary-percent < 100 -> send that share, watch the error budget, then promote or roll back.
set -euo pipefail
RG="$1"; APP="$2"; IMAGE="$3"; SUFFIX="$4"; CANARY="${5:-100}"
WATCH_SECONDS="${WATCH_SECONDS:-600}"

current=$(az containerapp revision list -g "$RG" -n "$APP" \
  --query "[?properties.active && properties.trafficWeight > \`0\`] | [0].name" -o tsv)

if [[ -n "$current" ]]; then
  # Pin traffic to the running revision so the new one starts at 0%.
  az containerapp ingress traffic set -g "$RG" -n "$APP" --revision-weight "$current=100" -o none
fi

az containerapp update -g "$RG" -n "$APP" --image "$IMAGE" --revision-suffix "$SUFFIX" -o none
new="$APP--$SUFFIX"

echo "Waiting for $new to become healthy..."
for _ in $(seq 1 60); do
  state=$(az containerapp revision show -g "$RG" -n "$APP" --revision "$new" --query "properties.healthState" -o tsv)
  [[ "$state" == "Healthy" ]] && break
  sleep 10
done
[[ "$state" == "Healthy" ]] || { echo "Revision $new is $state"; exit 1; }

# Revision-scoped URL for smoke tests before any user traffic reaches it.
az containerapp revision label add -g "$RG" -n "$APP" --revision "$new" --label candidate --no-prompt -o none
fqdn=$(az containerapp show -g "$RG" -n "$APP" --query "properties.configuration.ingress.fqdn" -o tsv)
"$(dirname "$0")/smoke-test.sh" "https://${fqdn/$APP/$APP---candidate}"

if [[ -z "$current" ]]; then
  az containerapp ingress traffic set -g "$RG" -n "$APP" --revision-weight "$new=100" -o none
  exit 0
fi

if (( CANARY < 100 )); then
  az containerapp ingress traffic set -g "$RG" -n "$APP" --revision-weight "$new=$CANARY" "$current=$((100 - CANARY))" -o none
  echo "Canary at $CANARY% for ${WATCH_SECONDS}s"
  sleep "$WATCH_SECONDS"
  failed=$(az monitor metrics list --resource "$(az containerapp show -g "$RG" -n "$APP" --query id -o tsv)" \
    --metric Requests --filter "statusCodeCategory eq '5xx' and revisionName eq '$new'" \
    --interval PT1M --aggregation Total --query "sum(value[0].timeseries[0].data[].total)" -o tsv 2>/dev/null || echo 0)
  if (( ${failed%.*} > 0 )); then
    echo "Canary produced $failed server errors: rolling back to $current"
    az containerapp ingress traffic set -g "$RG" -n "$APP" --revision-weight "$current=100" -o none
    exit 1
  fi
fi

az containerapp ingress traffic set -g "$RG" -n "$APP" --revision-weight "$new=100" -o none
az containerapp revision label remove -g "$RG" -n "$APP" --label candidate -o none || true
az containerapp revision deactivate -g "$RG" -n "$APP" --revision "$current" -o none
echo "$APP now serving $new"
