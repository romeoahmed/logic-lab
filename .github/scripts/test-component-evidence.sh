#!/usr/bin/env bash

set -Eeuo pipefail

script_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
test_directory="$(mktemp -d)"
trap 'rm -rf "$test_directory"' EXIT

run_case() (
  export evidence_scenario="$1"
  export evidence_call_log="$test_directory/$1.calls"
  # Exported into the child Bash that runs the production script.
  # shellcheck disable=SC2329
  dotnet() {
    local stage
    case "$1" in
      --info) printf 'test runtime\n'; return 0 ;;
      test) stage='test' ;;
      run)
        if [[ "$*" == *'corpus qualification/corpus-v1.json'* ]]; then
          stage=corpus
        else
          stage=manifest
        fi
        ;;
      *) return 99 ;;
    esac
    printf '%s\n' "$stage" >> "$evidence_call_log"
    [[ "$stage" != "$evidence_scenario" ]] || return 7
    if [[ "$stage" != test ]]; then printf '{}\n' > "${!#}"; fi
  }
  export -f dotnet
  bash "$script_directory/verify-component-evidence.sh" "$test_directory/$1"
)

for scenario in success test manifest corpus; do
  actual=0
  run_case "$scenario" > "$test_directory/output" 2>&1 || actual=$?
  expected=7
  if [[ "$scenario" == success ]]; then expected=0; fi
  if [[ "$actual" != "$expected" ]]; then
    printf '%s: expected exit %s, got %s\n' "$scenario" "$expected" "$actual" >&2
    cat "$test_directory/output" >&2
    exit 1
  fi
  case "$scenario" in
    test) expected_calls='test' ;;
    manifest) expected_calls=$'test\nmanifest' ;;
    *) expected_calls=$'test\nmanifest\ncorpus' ;;
  esac
  [[ "$(cat "$test_directory/$scenario.calls")" == "$expected_calls" ]]
  for file in source-commit.txt source-changes.txt dotnet-info.txt; do
    [[ -f "$test_directory/$scenario/$file" ]]
  done
  if [[ "$scenario" == success || "$scenario" == corpus ]]; then
    [[ -f "$test_directory/$scenario/manifest.json" ]]
  else
    [[ ! -e "$test_directory/$scenario/manifest.json" ]]
  fi
  if [[ "$scenario" == success ]]; then
    [[ -f "$test_directory/$scenario/corpus-v1.json" ]]
  else
    [[ ! -e "$test_directory/$scenario/corpus-v1.json" ]]
  fi
done

actual=0
bash "$script_directory/verify-component-evidence.sh" "$test_directory/success" > "$test_directory/output" 2>&1 || actual=$?
[[ "$actual" == 2 ]]
printf 'Component evidence orchestration: 5 scenarios passed.\n'
