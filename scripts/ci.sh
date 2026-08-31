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

selected_lane="quality-gate"

usage() {
  cat <<'EOF'
Usage: bash scripts/ci.sh [--lane <lane>]

Lanes:
  quality-gate      Run the full local PR gate (default).
  verify-fast       Run .NET and browser workstation lanes.
  verify-dotnet     Run restore, format, .NET build, and .NET tests.
  verify-browser    Run dashboard install, tests, and bundle build.
  verify-docs       Run docs, source, AI, and roadmap validation.
  verify-workflows  Run lane manifest and workflow hygiene validation.
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --lane)
      if [[ $# -lt 2 ]]; then
        echo "--lane requires a value." >&2
        exit 2
      fi
      selected_lane="$2"
      shift 2
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown argument: $1" >&2
      usage >&2
      exit 2
      ;;
  esac
done

python_cmd=""
for candidate in python3 python; do
  if command -v "$candidate" >/dev/null 2>&1 &&
    "$candidate" -c 'import sys; raise SystemExit(0 if sys.version_info >= (3, 11) else 1)' >/dev/null 2>&1; then
    python_cmd="$candidate"
    break
  fi
done

if [[ -z "$python_cmd" ]]; then
  echo "Python 3.11 or newer is required to run Meridian CI." >&2
  exit 127
fi

ci_summary_dir="artifacts/ci-summary/${selected_lane}"
ci_steps_tsv="${ci_summary_dir}/steps.tsv"
ci_summary_md="${ci_summary_dir}/summary.md"
handoff_summary_json="${ci_summary_dir}/ai-handoff-docs-automation-summary.json"
handoff_summary_md="${ci_summary_dir}/ai-handoff-docs-automation-summary.md"
mkdir -p "$ci_summary_dir" artifacts/build-logs artifacts/test-results/dotnet
rm -f artifacts/build-logs/*.log artifacts/test-results/dotnet/ci-dotnet-test-summary.json
: > "$ci_steps_tsv"

write_ci_summary() {
  local status=$?
  set +e
  local summary_args=(
    --lane "$selected_lane"
    --exit-code "$status"
    --steps-tsv "$ci_steps_tsv"
    --output "$ci_summary_md"
    --dotnet-summary-json artifacts/test-results/dotnet/ci-dotnet-test-summary.json
  )
  for log_path in artifacts/build-logs/*.log; do
    if [[ -f "$log_path" ]]; then
      summary_args+=(--build-log "$log_path")
    fi
  done
  if [[ -n "${GITHUB_STEP_SUMMARY:-}" ]]; then
    summary_args+=(--github-step-summary "$GITHUB_STEP_SUMMARY")
  fi
  "$python_cmd" build/scripts/ci/summarize-ci-artifacts.py "${summary_args[@]}" >/dev/null || true
  exit "$status"
}
trap write_ci_summary EXIT

run_step() {
  local name="$1"
  shift
  local start
  start=$(date +%s)
  echo "::group::${name}"
  set +e
  "$@"
  local status=$?
  set -e
  local end
  end=$(date +%s)
  local duration=$((end - start))
  printf '%s\t%s\t%s\n' "$name" "$status" "$duration" >> "$ci_steps_tsv"
  echo "::endgroup::"
  return "$status"
}

verify_toolchain_dotnet() {
  run_step "Verify .NET SDK" dotnet --info
  run_step "Verify Python" "$python_cmd" --version
}

verify_toolchain_browser() {
  run_step "Verify Node.js" node --version
  run_step "Verify npm" npm --version
  run_step "Verify Python" "$python_cmd" --version
}

verify_toolchain_docs() {
  run_step "Verify Python" "$python_cmd" --version
}

verify_dotnet() {
  verify_toolchain_dotnet

  run_step "Restore .NET solution" \
    dotnet restore Meridian.sln -p:EnableWindowsTargeting=true

  run_step "Verify .NET formatting" \
    dotnet format whitespace Meridian.sln --verify-no-changes --verbosity minimal --no-restore

  run_step "Validate warning suppression inventory" \
    "$python_cmd" build/scripts/ci/check-warning-suppressions.py

  run_step "Enforce ApiClientService caller ratchet" \
    "$python_cmd" build/scripts/ci/check-apiclient-callers.py

  run_step "Enforce no-new-god-file ratchet" \
    "$python_cmd" build/scripts/ci/check-file-size.py

  run_step "Enforce consolidated-helper duplication ratchet" \
    "$python_cmd" build/scripts/ci/check-duplicate-helpers.py

  run_step "Enforce inline SHA-256 hashing ratchet" \
    "$python_cmd" build/scripts/ci/check-inline-sha256.py

  run_step "Enforce posture-environment test serialization" \
    "$python_cmd" build/scripts/ci/check-posture-env-serialization.py

  run_step "Enforce server-derived ActionOrigin at endpoints" \
    "$python_cmd" build/scripts/ci/check-action-origin-derivation.py

  run_step "Enforce declared file-store concurrency postures" \
    "$python_cmd" build/scripts/ci/check-store-concurrency-posture.py

  run_step "Enforce ledger-book-native accounting scope" \
    "$python_cmd" build/scripts/ci/check-ledger-book-scope.py

  run_step "Enforce ledger dimension coverage across surfaces" \
    "$python_cmd" build/scripts/ci/check-ledger-dimension-coverage.py

  run_step "Build web workstation .NET lane" \
    bash -c 'set -euo pipefail; dotnet build Meridian.WebWorkstation.slnf -c Release --no-restore -p:EnableWindowsTargeting=true -p:UseAppHost=false 2>&1 | tee artifacts/build-logs/web-workstation-build.log'

  run_step "Run .NET non-integration test projects" \
    "$python_cmd" build/scripts/ci/run-dotnet-ci-tests.py \
      --configuration Release \
      --filter "Category!=Integration&Category!=Performance" \
      --results-dir artifacts/test-results/dotnet \
      --summary-output artifacts/test-results/dotnet/ci-dotnet-test-summary.md \
      --json-output artifacts/test-results/dotnet/ci-dotnet-test-summary.json
}

verify_browser() {
  verify_toolchain_browser

  run_step "Install dashboard dependencies from lockfile" \
    bash -c 'set -euo pipefail; npm --prefix src/Meridian.Ui/dashboard ci --include=optional 2>&1 | tee artifacts/build-logs/dashboard-install.log'

  run_step "Generated UI contract drift gate" \
    bash -c 'set -euo pipefail; "$0" build/scripts/generate-ui-api-routes-ts.py --check 2>&1 | tee artifacts/build-logs/ui-api-routes-check.log; "$0" build/scripts/generate-workspace-catalog-ts.py --check 2>&1 | tee artifacts/build-logs/workspace-catalog-check.log' "$python_cmd"

  run_step "Lint dashboard source" \
    bash -c 'set -euo pipefail; npm --prefix src/Meridian.Ui/dashboard run lint 2>&1 | tee artifacts/build-logs/dashboard-lint.log'

  run_step "Enforce strictNullChecks on dashboard source" \
    bash -c 'set -euo pipefail; npm --prefix src/Meridian.Ui/dashboard run typecheck:strict 2>&1 | tee artifacts/build-logs/dashboard-typecheck-strict.log'

  run_step "Run dashboard tests" \
    bash -c 'set -euo pipefail; npm --prefix src/Meridian.Ui/dashboard run test 2>&1 | tee artifacts/build-logs/dashboard-test.log'

  run_step "Build dashboard bundle" \
    bash -c 'set -euo pipefail; npm --prefix src/Meridian.Ui/dashboard run build 2>&1 | tee artifacts/build-logs/dashboard-build.log'

  # PRD-018 freshness gate: the tracked canonical bundle must match what the current dashboard
  # source builds, so the repo-launch demo can never serve stale assets.
  run_step "Workstation bundle freshness gate" \
    bash -c 'set -euo pipefail; \
      if [ -n "$(git status --porcelain -- src/Meridian.Ui/wwwroot/workstation)" ]; then \
        echo "The tracked workstation bundle lags src/Meridian.Ui/dashboard." >&2; \
        echo "Run: npm --prefix src/Meridian.Ui/dashboard run build" >&2; \
        echo "then commit the regenerated src/Meridian.Ui/wwwroot/workstation tree." >&2; \
        git status --porcelain -- src/Meridian.Ui/wwwroot/workstation >&2; \
        exit 1; \
      fi'
}

verify_docs() {
  verify_toolchain_docs

  # TypeScript resolves an ambiguous star export by exporting neither declaration, so a
  # duplicated DTO silently disappears from '@/types' rather than conflicting.
  run_step "Validate dashboard type barrel" \
    "$python_cmd" build/scripts/ci/check-dashboard-type-barrel.py --summary

  # The barrel gate proves each TypeScript name is declared once; it cannot tell whether that
  # declaration still matches the C# record the API serialises. The dashboard casts parsed JSON to
  # its interface, so a renamed or newly-nullable C# member reaches the browser as a silently
  # missing field rather than a compile error.
  run_step "Validate C#/TypeScript contract parity" \
    "$python_cmd" build/scripts/ci/check-contract-type-parity.py

  # An alert whose expr names a series the exporter never emits can never fire, and a
  # runbook link that does not resolve strands the responder. Both used to be invisible.
  run_step "Validate observability contract" \
    "$python_cmd" build/scripts/ci/validate-observability-contract.py --summary

  # The shipped sample config is what operators copy first. A provider value that is not a
  # DataSourceKind member throws at startup because the converter fails closed, and a
  # secret-shaped key teaches users to keep credentials in JSON the sample itself disclaims.
  run_step "Validate sample config data sources" \
    "$python_cmd" build/scripts/ci/check-sample-config-datasources.py

  # The contract gate is static. It cannot tell whether a rule fires on the condition it
  # claims, which is where every monitoring regression here has actually lived, so promtool
  # runs the rule unit tests and `docker compose config` renders the deployed stacks.
  # --allow-missing-tools keeps a local run useful; CI installs both and must not pass it.
  local monitoring_tool_policy=()
  if [[ -z "${GITHUB_ACTIONS:-}" ]]; then
    monitoring_tool_policy+=(--allow-missing-tools)
  fi
  run_step "Validate monitoring deployment" \
    "$python_cmd" build/scripts/ci/validate-monitoring-deployment.py --summary "${monitoring_tool_policy[@]}"

  run_step "Validate status docs delivery claims" \
    bash -c '"$0" scripts/check_status_delivery_claims.py && "$0" -m unittest tests/scripts/test_check_status_delivery_claims.py' "$python_cmd"

  run_step "Validate status doc staleness" \
    "$python_cmd" scripts/check_status_doc_staleness.py

  # The agent validator needs PyYAML. Check for it here and name the install command
  # rather than pip-installing: this script runs against a developer's own interpreter,
  # and silently mutating it is worse than a failure that says what to do. The hosted
  # lanes install it explicitly instead.
  run_step "Check docs automation dependencies" \
    bash -c '"$0" -c "import yaml" 2>/dev/null || { echo "PyYAML is required by build/scripts/docs/validate-agent-definitions.py." >&2; echo "  $0 -m pip install --requirement build/scripts/docs/requirements.txt" >&2; exit 1; }' "$python_cmd"

  # Runs here rather than only in the docs-automation profile: the Documentation
  # Automation workflow is path-filtered, so a change touching only .claude/agents/**
  # would otherwise land without any hosted check resolving its tool declarations.
  run_step "Validate Claude agent definitions" \
    bash -c '"$0" build/scripts/docs/validate-agent-definitions.py && "$0" -m unittest tests/scripts/test_validate_agent_definitions.py' "$python_cmd"

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
      --json-output "$handoff_summary_json" \
      --summary-output "$handoff_summary_md"

  # Mirrors the quality-gate step of the same name: regenerate the whole-repo documentation
  # artifacts and reject drift, so adding a file outside docs/** cannot silently red the weekly
  # Production Certification documentation job.
  run_step "Reject whole-repo generated documentation drift" \
    "$python_cmd" build/scripts/docs/run-docs-automation.py \
      --scripts generate-structure-docs,generate-health-dashboard,generate-workflow-manifest

  # The automation profile runs generate-structure-docs in structure mode only, so the workflows
  # overview it also owns needs its own invocation.
  run_step "Regenerate the workflows overview" \
    "$python_cmd" build/scripts/docs/generate-structure-docs.py --workflows-only

  run_step "Verify whole-repo generated documentation is committed" \
    git diff --exit-code -- \
      docs/generated/repository-structure.md \
      docs/generated/workflows-overview.md \
      docs/status/doc-health-dashboard.json \
      docs/status/doc-health-dashboard.md \
      docs/status/workflow-drift-report.md

  run_step "Enforce mode escalation policy" \
    "$python_cmd" build/scripts/docs/check-mode-escalation.py \
      --route-json docs/status/prompt-route-lint-report.json \
      --summary-json "$handoff_summary_json" \
      --summary

  run_step "Enforce validation-floor guard for AI/docs changes" \
    "$python_cmd" build/scripts/docs/check-validation-floor.py \
      --summary-json "$handoff_summary_json" \
      --route-json docs/status/prompt-route-lint-report.json \
      --summary

  run_step "Validate roadmap registry" \
    "$python_cmd" build/scripts/docs/validate-roadmap-registry.py --summary

  run_step "Validate source READMEs" \
    "$python_cmd" build/scripts/docs/validate-source-readmes.py --summary

  run_step "Scan source TODOs" \
    "$python_cmd" build/scripts/docs/scan-source-todos.py --summary

  run_step "Render roadmap/source docs and check drift" \
    bash -c 'set -euo pipefail; { "$0" build/scripts/docs/render-roadmap-docs.py --summary && "$0" build/scripts/docs/render-source-docs.py --summary && [ -z "$(git status --porcelain -- docs/roadmap/generated docs/source/generated docs/status/ROADMAP_SUMMARY.md src)" ]; } 2>&1 | tee artifacts/build-logs/docs-render-drift.log' "$python_cmd"
}

verify_workflows() {
  verify_toolchain_docs

  run_step "Validate lane manifest" \
    "$python_cmd" build/scripts/ci/check-lane-manifest.py --summary

  run_step "Validate lane vocabulary" \
    "$python_cmd" build/scripts/docs/check-known-lanes.py

  run_step "Validate workflow hygiene" \
    bash -c 'set -euo pipefail; "$0" build/scripts/ci/check-workflow-hygiene.py 2>&1 | tee artifacts/build-logs/workflow-hygiene.log' "$python_cmd"

  # A skipped test reports the same green as a passing one, so every skip must name an
  # owner, a category, and a review date that expires.
  run_step "Validate test skip register" \
    "$python_cmd" build/scripts/ci/check-test-skip-register.py --summary

  # Audit finding P9: ~65 of 75 tests/scripts suites were wired to no CI lane and several
  # had rotted unnoticed. The runner gates every suite except the tracked quarantine list
  # in build/scripts/ci/script-test-quarantine.json, which it prints on every run.
  run_step "Run repo script test suite (quarantine-aware)" \
    "$python_cmd" build/scripts/ci/run-script-tests.py
}

verify_fast() {
  verify_dotnet
  verify_browser
}

quality_gate() {
  verify_dotnet
  verify_browser
  verify_docs
  verify_workflows
}

case "$selected_lane" in
  quality-gate)
    quality_gate
    ;;
  verify-fast)
    verify_fast
    ;;
  verify-dotnet)
    verify_dotnet
    ;;
  verify-browser)
    verify_browser
    ;;
  verify-docs)
    verify_docs
    ;;
  verify-workflows)
    verify_workflows
    ;;
  *)
    echo "Unknown CI lane '$selected_lane'." >&2
    usage >&2
    exit 2
    ;;
esac

echo "Meridian CI lane '$selected_lane' completed successfully."
