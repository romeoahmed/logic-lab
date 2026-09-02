#!/usr/bin/env bash

set -Eeuo pipefail

readonly POLL_ATTEMPTS=180
readonly POLL_INTERVAL_SECONDS=10
readonly AZURE_QUERY_ATTEMPTS=3

require_environment() {
  local name

  for name in AZURE_RESOURCE_GROUP BOOTSTRAP_JOB LOG_ANALYTICS_WORKSPACE_ID MIGRATION_JOB; do
    if [[ -z "${!name:-}" ]]; then
      printf 'Required environment variable %s is empty.\n' "$name" >&2
      return 1
    fi
  done
}

show_job_logs() {
  local attempt
  local execution_name="$1"
  local logs
  local query

  query="ContainerAppConsoleLogs_CL | where TimeGenerated > ago(1h) | where ContainerGroupName_s startswith '$execution_name-' | project TimeGenerated, Log_s | order by TimeGenerated asc | take 300"

  for ((attempt = 1; attempt <= AZURE_QUERY_ATTEMPTS; attempt++)); do
    if logs="$(az monitor log-analytics query \
      --workspace "$LOG_ANALYTICS_WORKSPACE_ID" \
      --analytics-query "$query" \
      --output tsv)" && [[ -n "$logs" ]]; then
      printf '%s\n' "$logs"
      return
    fi

    if ((attempt < AZURE_QUERY_ATTEMPTS)); then
      sleep "$POLL_INTERVAL_SECONDS"
    fi
  done

  printf 'Container logs are not available yet; query execution %s in Log Analytics.\n' "$execution_name" >&2
}

query_execution_status() {
  local attempt
  local execution_name="$1"
  local job_name="$2"
  local status

  for ((attempt = 1; attempt <= AZURE_QUERY_ATTEMPTS; attempt++)); do
    if status="$(az containerapp job execution show \
      --name "$job_name" \
      --resource-group "$AZURE_RESOURCE_GROUP" \
      --job-execution-name "$execution_name" \
      --query properties.status \
      --output tsv)"; then
      printf '%s\n' "$status"
      return
    fi

    if ((attempt < AZURE_QUERY_ATTEMPTS)); then
      sleep "$POLL_INTERVAL_SECONDS"
    fi
  done

  printf 'Unable to read execution %s for job %s.\n' "$execution_name" "$job_name" >&2
  return 1
}

run_job() {
  local attempt
  local execution_name
  local job_name="$1"
  local status

  execution_name="$(az containerapp job start \
    --name "$job_name" \
    --resource-group "$AZURE_RESOURCE_GROUP" \
    --query name \
    --output tsv)"

  if [[ -z "$execution_name" ]]; then
    printf 'Azure did not return an execution name for %s.\n' "$job_name" >&2
    return 1
  fi

  for ((attempt = 1; attempt <= POLL_ATTEMPTS; attempt++)); do
    if ! status="$(query_execution_status "$execution_name" "$job_name")"; then
      show_job_logs "$execution_name"
      return 1
    fi

    case "$status" in
      Succeeded)
        printf '%s execution %s succeeded.\n' "$job_name" "$execution_name"
        return
        ;;
      Failed | Stopped | Degraded)
        printf '%s execution %s ended with status %s.\n' "$job_name" "$execution_name" "$status" >&2
        show_job_logs "$execution_name"
        return 1
        ;;
    esac

    sleep "$POLL_INTERVAL_SECONDS"
  done

  printf '%s execution %s timed out.\n' "$job_name" "$execution_name" >&2
  show_job_logs "$execution_name"
  return 1
}

main() {
  require_environment
  run_job "$BOOTSTRAP_JOB"
  run_job "$MIGRATION_JOB"
  run_job "$BOOTSTRAP_JOB"
}

main "$@"
