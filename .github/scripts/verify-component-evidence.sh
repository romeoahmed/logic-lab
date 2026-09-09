#!/usr/bin/env bash

set -Eeuo pipefail

if [[ $# != 1 || -e "$1" ]]; then
  printf 'Usage: verify-component-evidence.sh <new-output-directory>\n' >&2
  exit 2
fi

evidence_directory="$1"
mkdir -p "$evidence_directory"
evidence_directory="$(cd "$evidence_directory" && pwd)"
git rev-parse HEAD > "$evidence_directory/source-commit.txt"
git status --porcelain > "$evidence_directory/source-changes.txt"
dotnet --info > "$evidence_directory/dotnet-info.txt"

# Fresh reports from this checkout are required; TRX itself has no source digest.
dotnet test --solution logic-lab.slnx --configuration Release --no-build --no-restore \
  --report-trx --results-directory "$evidence_directory/reports"
dotnet run --project tools/LogicLab.Conformance --configuration Release --no-build --no-restore -- \
  conformance/manifest.json conformance/evidence.json "$evidence_directory/reports" \
  "$evidence_directory/manifest.json"
dotnet run --project tools/LogicLab.Conformance --configuration Release --no-build --no-restore -- \
  corpus qualification/corpus-v1.json "$evidence_directory/reports" \
  "$evidence_directory/corpus-v1.json"
