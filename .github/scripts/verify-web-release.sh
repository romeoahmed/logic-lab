#!/usr/bin/env bash

set -Eeuo pipefail

verify_revision() {
  local app
  local revision

  app="$(az containerapp show --name "$WEB_APP_NAME" --resource-group "$AZURE_RESOURCE_GROUP" --output json)" || return 1
  revision="$(az containerapp revision show --name "$WEB_APP_NAME" --resource-group "$AZURE_RESOURCE_GROUP" --revision "$WEB_REVISION_NAME" --output json)" || return 1

  # The public endpoint can still serve the previous healthy revision during rollout.
  jq -e --arg revision "$WEB_REVISION_NAME" --arg host "$WEB_FQDN" '
    .properties.latestRevisionName == $revision and
    .properties.latestReadyRevisionName == $revision and
    .properties.configuration.activeRevisionsMode == "Single" and
    .properties.configuration.ingress.fqdn == $host
  ' <<< "$app" >/dev/null &&
    jq -e --arg image "$WEB_IMAGE" '
      .properties.active == true and
      [.properties.template.containers[] | select(.name == "web") | .image] == [$image]
    ' <<< "$revision" >/dev/null
}

verify_ready() {
  verify_revision &&
    curl --fail --silent --show-error --max-time 10 "https://$WEB_FQDN/health/ready" >/dev/null
}

main() {
  local name
  local attempt
  for name in AZURE_RESOURCE_GROUP WEB_APP_NAME WEB_REVISION_NAME WEB_FQDN WEB_IMAGE; do
    if [[ -z "${!name:-}" ]]; then
      printf 'Required environment variable %s is empty.\n' "$name" >&2
      return 1
    fi
  done

  for ((attempt = 1; attempt <= 60; attempt++)); do
    if verify_ready; then
      for _ in 1 2 3; do
        sleep 10
        verify_ready || return 1
      done
      return
    fi
    sleep 10
  done

  printf 'The target Web revision did not become ready within 60 checks.\n' >&2
  return 1
}

if [[ "${BASH_SOURCE[0]}" == "$0" ]]; then
  main "$@"
fi
