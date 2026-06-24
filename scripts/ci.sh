#!/usr/bin/env bash
set -Eeuo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

export CI="${CI:-true}"
export DOTNET_CLI_TELEMETRY_OPTOUT="${DOTNET_CLI_TELEMETRY_OPTOUT:-1}"
export DOTNET_GENERATE_ASPNET_CERTIFICATE="${DOTNET_GENERATE_ASPNET_CERTIFICATE:-false}"
export DOTNET_NOLOGO="${DOTNET_NOLOGO:-true}"
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE="${DOTNET_SKIP_FIRST_TIME_EXPERIENCE:-1}"
export MERIDIAN_DISABLE_DOCKER_TESTS="${MERIDIAN_DISABLE_DOCKER_TESTS:-true}"

python_cmd=""
for candidate in python3 python; do
  if command -v "$candidate" >/dev/null 2>&1 &&
    "$candidate" -c 'import sys; raise SystemExit(0 if sys.version_info >= (3, 11) else 1)' >/dev/null 2>&1; then
    python_cmd="$candidate"
    break
  fi
done

if [ -z "$python_cmd" ]; then
  echo "Python 3.11 or newer is required to run Meridian CI." >&2
  exit 127
fi

run_step() {
  local name="$1"
  shift
  echo "::group::${name}"
  set +e
  "$@"
  local status=$?
  set -e
  echo "::endgroup::"
  return "$status"
}

run_step "Verify toolchain" dotnet --info
run_step "Verify Node.js" node --version
run_step "Verify npm" npm --version
run_step "Verify Python" "$python_cmd" --version

run_step "Restore .NET solution" \
  dotnet restore Meridian.sln -p:EnableWindowsTargeting=true

run_step "Verify .NET formatting" \
  dotnet format whitespace Meridian.sln --verify-no-changes --verbosity minimal --no-restore

run_step "Validate warning suppression inventory" \
  "$python_cmd" build/scripts/ci/check-warning-suppressions.py

mkdir -p artifacts/build-logs artifacts/test-results/dotnet
run_step "Build web workstation .NET lane" \
  bash -c 'set -euo pipefail; dotnet build Meridian.WebWorkstation.slnf -c Release --no-restore -p:EnableWindowsTargeting=true -p:UseAppHost=false 2>&1 | tee artifacts/build-logs/web-workstation-build.log'

run_step "Run .NET non-integration test projects" \
  "$python_cmd" build/scripts/ci/run-dotnet-ci-tests.py \
    --configuration Release \
    --filter "Category!=Integration&Category!=Performance" \
    --results-dir artifacts/test-results/dotnet \
    --summary-output artifacts/test-results/dotnet/ci-dotnet-test-summary.md \
    --json-output artifacts/test-results/dotnet/ci-dotnet-test-summary.json

run_step "Install dashboard dependencies from lockfile" \
  npm ci --prefix src/Meridian.Ui/dashboard --include=optional

run_step "Run dashboard tests" \
  npm --prefix src/Meridian.Ui/dashboard run test

run_step "Build dashboard bundle" \
  npm --prefix src/Meridian.Ui/dashboard run build

run_step "Validate status docs delivery claims" \
  bash -c '"$0" scripts/check_status_delivery_claims.py && "$0" -m unittest tests/scripts/test_check_status_delivery_claims.py' "$python_cmd"

run_step "Validate status doc staleness" \
  "$python_cmd" scripts/check_status_doc_staleness.py

run_step "Validate provider-validation script tests" \
  bash -c '"$0" -m unittest tests/scripts/test_generate_dk1_pilot_parity_packet.py && "$0" -m unittest tests/scripts/test_prepare_dk1_operator_signoff.py' "$python_cmd"

run_step "Validate TODO registry contract" \
  bash -c '"$0" build/scripts/docs/scan-todos.py --json-output docs/status/todo-scan-results.json && "$0" build/scripts/docs/validate-todo-registry.py --scan-json docs/status/todo-scan-results.json --registry docs/source/todo-registry.json --enforce-prefix docs/source/' "$python_cmd"

run_step "Validate AI contract drift" \
  "$python_cmd" build/scripts/docs/check-ai-contract-drift.py \
    --canonical docs/ai/contract-policy.json \
    --mirror docs/ai/copilot/contract-policy.mirror.json \
    --mirror docs/ai/claude/contract-policy.mirror.json \
    --routing-rules docs/ai/codex/prompt-route-rules.json \
    --routing-host-doc docs/ai/codex/README.md \
    --routing-host-doc docs/ai/assistant-workflow-contract.md

run_step "Validate AI navigation freshness" \
  "$python_cmd" build/scripts/docs/check-ai-navigation-freshness.py \
    --navigation-json docs/ai/generated/repo-navigation.json \
    --max-age-days 14

run_step "Validate lane vocabulary" \
  "$python_cmd" build/scripts/docs/check-known-lanes.py

run_step "Validate AI handoff checklist schema" \
  "$python_cmd" build/scripts/docs/run-docs-automation.py \
    --scripts check-ai-handoff-strict,prompt-route-linter,handoff-packet-generator,check-handoff-packet-schema,check-ai-routing-parity \
    --json-output docs/status/docs-automation-summary.json \
    --summary-output docs/status/docs-automation-summary.md

run_step "Enforce mode escalation policy" \
  "$python_cmd" build/scripts/docs/check-mode-escalation.py \
    --route-json docs/status/prompt-route-lint-report.json \
    --summary-json docs/status/docs-automation-summary.json \
    --summary

run_step "Enforce validation-floor guard for AI/docs changes" \
  "$python_cmd" build/scripts/docs/check-validation-floor.py \
    --summary-json docs/status/docs-automation-summary.json \
    --route-json docs/status/prompt-route-lint-report.json \
    --summary

run_step "Validate roadmap registry" \
  "$python_cmd" build/scripts/docs/validate-roadmap-registry.py --summary

run_step "Validate source READMEs" \
  "$python_cmd" build/scripts/docs/validate-source-readmes.py --summary

run_step "Scan source TODOs" \
  "$python_cmd" build/scripts/docs/scan-source-todos.py --summary

run_step "Render roadmap/source docs and check drift" \\
  bash -c '"$0" build/scripts/docs/render-roadmap-docs.py --summary && "$0" build/scripts/docs/render-source-docs.py --summary && [ -z "$(git status --porcelain -- docs/roadmap/generated docs/source/generated docs/status/ROADMAP_SUMMARY.md src)" ]' "$python_cmd"

echo "Meridian CI completed successfully."
