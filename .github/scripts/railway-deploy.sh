#!/usr/bin/env bash
# Points the Railway service at one exact image and deploys it, then waits for the result.
#
# Why not `railway redeploy`: that re-runs the service's previous deployment with the image
# it already had, so a freshly pushed :latest is never picked up. The service sat on the
# 2026-08-06 build through every "successful" deploy until Nov 2026 because of it.
#
# Env:
#   RAILWAY_TOKEN       project token (project → Settings → Tokens); scoped to one environment
#   RAILWAY_SERVICE_ID  the service's ID or its name
#   DEPLOY_IMAGE        full image reference to deploy, e.g. ghcr.io/alp-balaj/expensetracker:deploy-123
#   DEPLOY_TIMEOUT      seconds to wait for the deployment (default 600)
set -euo pipefail

: "${RAILWAY_TOKEN:?RAILWAY_TOKEN is not set}"
: "${RAILWAY_SERVICE_ID:?RAILWAY_SERVICE_ID is not set}"
: "${DEPLOY_IMAGE:?DEPLOY_IMAGE is not set}"
TIMEOUT="${DEPLOY_TIMEOUT:-600}"
API=https://backboard.railway.com/graphql/v2

# gql QUERY [VARIABLES_JSON] -> prints .data, fails on GraphQL errors.
gql() {
  local body resp vars="${2:-}"
  [ -n "$vars" ] || vars='{}'
  body=$(jq -nc --arg q "$1" --argjson v "$vars" '{query: $q, variables: $v}')
  resp=$(curl -sS --fail-with-body "$API" \
    -H "Content-Type: application/json" \
    -H "Project-Access-Token: $RAILWAY_TOKEN" \
    -d "$body")
  if jq -e '.errors' >/dev/null <<<"$resp"; then
    echo "::error::Railway API: $(jq -c '.errors | map(.message)' <<<"$resp")" >&2
    return 1
  fi
  jq -c '.data' <<<"$resp"
}

tok=$(gql 'query { projectToken { projectId environmentId } }')
PROJECT_ID=$(jq -r '.projectToken.projectId' <<<"$tok")
ENV_ID=$(jq -r '.projectToken.environmentId' <<<"$tok")
echo "Project $PROJECT_ID, environment $ENV_ID"

# DEPLOY.md lets RAILWAY_SERVICE_ID be a name or an ID; the API only takes IDs.
services=$(gql 'query($p: String!) { project(id: $p) { services { edges { node { id name } } } } }' \
  "$(jq -nc --arg p "$PROJECT_ID" '{p: $p}')")
SERVICE_ID=$(jq -r --arg s "$RAILWAY_SERVICE_ID" \
  '.project.services.edges[].node | select(.id == $s or .name == $s) | .id' <<<"$services" | head -n1)
if [ -z "$SERVICE_ID" ]; then
  echo "::error::No service matching RAILWAY_SERVICE_ID in this project. Services: $(jq -c '[.project.services.edges[].node.name]' <<<"$services")"
  exit 1
fi
echo "Service $SERVICE_ID"

vars=$(jq -nc --arg s "$SERVICE_ID" --arg e "$ENV_ID" --arg i "$DEPLOY_IMAGE" '{s: $s, e: $e, i: $i}')
gql 'mutation($s: String!, $e: String!, $i: String!) {
  serviceInstanceUpdate(serviceId: $s, environmentId: $e, input: { source: { image: $i } })
}' "$vars" >/dev/null
echo "Source image set to $DEPLOY_IMAGE"

dep=$(gql 'mutation($s: String!, $e: String!) { serviceInstanceDeployV2(serviceId: $s, environmentId: $e) }' \
  "$(jq -nc --arg s "$SERVICE_ID" --arg e "$ENV_ID" '{s: $s, e: $e}')")
DEPLOYMENT_ID=$(jq -r '.serviceInstanceDeployV2' <<<"$dep")
echo "Deployment $DEPLOYMENT_ID started"

deadline=$(( $(date +%s) + TIMEOUT ))
last=""
while :; do
  status=$(gql 'query($d: String!) { deployment(id: $d) { status } }' \
    "$(jq -nc --arg d "$DEPLOYMENT_ID" '{d: $d}')" | jq -r '.deployment.status')
  [ "$status" != "$last" ] && echo "status: $status" && last=$status
  case "$status" in
    SUCCESS) echo "Deployed $DEPLOY_IMAGE"; exit 0 ;;
    FAILED|CRASHED|REMOVED|SKIPPED)
      echo "::error::Railway deployment $DEPLOYMENT_ID ended $status. Check its logs in the Railway dashboard."
      exit 1 ;;
  esac
  if [ "$(date +%s)" -ge "$deadline" ]; then
    echo "::error::Deployment $DEPLOYMENT_ID still $status after ${TIMEOUT}s."
    exit 1
  fi
  sleep 10
done
