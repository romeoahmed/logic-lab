#!/usr/bin/env bash

set -Eeuo pipefail

script_directory="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
test_directory="$(mktemp -d)"
trap 'rm -rf -- "$test_directory"' EXIT

run_case() (
  local scenario="$1"
  # shellcheck source=verify-web-release.sh
  source "$script_directory/verify-web-release.sh"
  export AZURE_RESOURCE_GROUP=test WEB_APP_NAME=web WEB_REVISION_NAME=web--new
  export WEB_FQDN=web.example.invalid WEB_IMAGE=registry.example.invalid/web@sha256:abc
  local fixture_app='{"properties":{"latestRevisionName":"web--new","latestReadyRevisionName":"web--new","configuration":{"activeRevisionsMode":"Single","ingress":{"fqdn":"web.example.invalid"}}}}'
  local fixture_revision='{"properties":{"active":true,"template":{"containers":[{"name":"web","image":"registry.example.invalid/web@sha256:abc"}]}}}'
  case "$scenario" in
    old-ready) fixture_app="$(jq '.properties.latestReadyRevisionName = "web--old"' <<< "$fixture_app")" ;;
    replaced) fixture_app="$(jq '.properties.latestRevisionName = "web--other"' <<< "$fixture_app")" ;;
    wrong-image) fixture_revision="$(jq '.properties.template.containers[0].image = "wrong"' <<< "$fixture_revision")" ;;
    inactive) fixture_revision="$(jq '.properties.active = false' <<< "$fixture_revision")" ;;
    multiple) fixture_app="$(jq '.properties.configuration.activeRevisionsMode = "Multiple"' <<< "$fixture_app")" ;;
    wrong-host) fixture_app="$(jq '.properties.configuration.ingress.fqdn = "other.example.invalid"' <<< "$fixture_app")" ;;
    missing-input) unset WEB_REVISION_NAME ;;
  esac

  az() {
    [[ "$scenario" != azure-error ]] || return 1
    if [[ "$1 $2" == 'containerapp show' ]]; then
      if [[ "$scenario" == pending && ! -e "$test_directory/observed" ]]; then
        touch "$test_directory/observed"
        jq '.properties.latestReadyRevisionName = "web--old"' <<< "$fixture_app"
      else
        printf '%s\n' "$fixture_app"
      fi
    elif [[ "$1 $2 $3" == 'containerapp revision show' ]]; then
      printf '%s\n' "$fixture_revision"
    else
      return 1
    fi
  }
  curl() {
    [[ "$scenario" != http-error ]] || return 1
    if [[ "$scenario" == unstable ]]; then
      [[ ! -e "$test_directory/served" ]] || return 1
      touch "$test_directory/served"
    fi
  }
  sleep() { :; }

  main
)

for scenario in ready pending old-ready replaced wrong-image inactive multiple wrong-host missing-input azure-error http-error unstable; do
  expected=1
  if [[ "$scenario" == ready || "$scenario" == pending ]]; then expected=0; fi
  actual=0
  run_case "$scenario" >"$test_directory/output" 2>&1 || actual=$?
  if [[ "$actual" != "$expected" ]]; then
    printf '%s: expected exit %s, got %s\n' "$scenario" "$expected" "$actual" >&2
    cat "$test_directory/output" >&2
    exit 1
  fi
done

printf 'Web release verification: 12 scenarios passed.\n'
