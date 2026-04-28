#!/usr/bin/env bash
set -Eeuo pipefail

log() {
    printf '\n[maint-full] %s\n' "$*"
}

warn() {
    printf '\n[maint-full:warn] %s\n' "$*" >&2
}

err() {
    printf '\n[maint-full:error] %s\n' "$*" >&2
}

have() {
    command -v "$1" >/dev/null 2>&1
}

ROOT_DIR="$(git rev-parse --show-toplevel 2>/dev/null || pwd)"
cd "$ROOT_DIR"

[[ -f Meridian.sln ]] || {
    err "Meridian.sln not found"
    exit 1
}

mkdir -p .ai logs data
STATUS_FILE=".ai/maintenance-status.json"
SUMMARY_FILE=".ai/MAINTENANCE_STATUS.md"
TMP_STEPS="$(mktemp)"

if [[ -f .ai/env.sh ]]; then
    # shellcheck disable=SC1091
    source .ai/env.sh
fi

record_step() {
    local name="$1"
    local status="$2"
    local details="${3:-}"
    printf '{"name":"%s","status":"%s","details":"%s"}\n' \
        "$name" "$status" "$(printf '%s' "$details" | tr '"' "'" )" >>"$TMP_STEPS"
}

run_step() {
    local name="$1"
    shift
    log "$name"
    if "$@"; then
        record_step "$name" "passed"
    else
        warn "Failed: $name"
        record_step "$name" "failed" "command failed"
        return 1
    fi
}

DOTNET_AVAILABLE=false
NODE_AVAILABLE=false
PYTHON_AVAILABLE=false
MAKE_AVAILABLE=false

have dotnet && DOTNET_AVAILABLE=true
have node && have npm && NODE_AVAILABLE=true
have python3 && PYTHON_AVAILABLE=true
have make && MAKE_AVAILABLE=true

if [[ "$DOTNET_AVAILABLE" != true ]]; then
    err "dotnet is required for full maintenance"
    exit 1
fi

if [[ "$PYTHON_AVAILABLE" == true && -f build/scripts/ai-repo-updater.py ]]; then
    run_step "known-errors" python3 build/scripts/ai-repo-updater.py known-errors
    run_step "diff-summary" python3 build/scripts/ai-repo-updater.py diff-summary
fi

if [[ "$NODE_AVAILABLE" == true && -f package-lock.json ]]; then
    run_step "npm-ci" npm ci
fi

run_step "dotnet-restore" dotnet restore Meridian.sln /p:EnableWindowsTargeting=true --verbosity minimal
run_step "dotnet-build" dotnet build Meridian.sln -c Release --no-restore --nologo /p:EnableWindowsTargeting=true
test_common_args=(-c Release --no-build --nologo /p:EnableWindowsTargeting=true)
test_filtered_args=("${test_common_args[@]}" --filter "Category!=Integration")

run_step "dotnet-test-backtesting" dotnet test tests/Meridian.Backtesting.Tests/Meridian.Backtesting.Tests.csproj "${test_filtered_args[@]}"
run_step "dotnet-test-fsharp" dotnet test tests/Meridian.FSharp.Tests/Meridian.FSharp.Tests.fsproj "${test_common_args[@]}"
run_step "dotnet-test-main" dotnet test tests/Meridian.Tests/Meridian.Tests.csproj "${test_filtered_args[@]}"
run_step "dotnet-test-ui" dotnet test tests/Meridian.Ui.Tests/Meridian.Ui.Tests.csproj "${test_filtered_args[@]}"
run_step "dotnet-test-mcpserver" dotnet test tests/Meridian.McpServer.Tests/Meridian.McpServer.Tests.csproj "${test_filtered_args[@]}"
run_step "dotnet-test-directlending" dotnet test tests/Meridian.DirectLending.Tests/Meridian.DirectLending.Tests.csproj "${test_filtered_args[@]}"
run_step "dotnet-test-fundstructure" dotnet test tests/Meridian.FundStructure.Tests/Meridian.FundStructure.Tests.csproj "${test_filtered_args[@]}"
run_step "dotnet-test-quantscript" dotnet test tests/Meridian.QuantScript.Tests/Meridian.QuantScript.Tests.csproj "${test_filtered_args[@]}"
record_step "dotnet-test-wpf" "skipped" "WPF tests require the desktop validation lane and are not run by Linux full maintenance."

if [[ "$MAKE_AVAILABLE" == true && -f Makefile ]]; then
    grep -qE '^[[:space:]]*doctor:' Makefile 2>/dev/null && \
        run_step "doctor" make doctor
    grep -qE '^[[:space:]]*ai-verify:' Makefile 2>/dev/null && \
        run_step "ai-verify" make ai-verify
fi

STEPS_JSON="$(paste -sd, "$TMP_STEPS")"
rm -f "$TMP_STEPS"

cat >"$STATUS_FILE" <<EOF
{
  "timestamp": "$(date -u +"%Y-%m-%dT%H:%M:%SZ")",
  "mode": "full",
  "repo_root": "$ROOT_DIR",
  "environment": {
    "dotnet_available": $DOTNET_AVAILABLE,
    "node_available": $NODE_AVAILABLE,
    "python_available": $PYTHON_AVAILABLE,
    "make_available": $MAKE_AVAILABLE
  },
  "steps": [${STEPS_JSON}],
  "summary": {
    "status": "completed",
    "next_action": "Review failed steps and uploaded artifacts if any."
  }
}
EOF

cat >"$SUMMARY_FILE" <<EOF
# Meridian Maintenance Status

- Mode: full
- Timestamp: $(date -u +"%Y-%m-%dT%H:%M:%SZ")
- Repo root: $ROOT_DIR
- dotnet available: $DOTNET_AVAILABLE

Artifacts:
- .ai/maintenance-status.json
EOF

log "Full maintenance complete"
