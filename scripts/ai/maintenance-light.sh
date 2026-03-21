#!/usr/bin/env bash
set -Eeuo pipefail

log() {
    printf '\n[maint-light] %s\n' "$*"
}

warn() {
    printf '\n[maint-light:warn] %s\n' "$*" >&2
}

err() {
    printf '\n[maint-light:error] %s\n' "$*" >&2
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
        record_step "$name" "warning" "command failed"
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

if [[ "$PYTHON_AVAILABLE" == true && -f build/scripts/ai-repo-updater.py ]]; then
    run_step "known-errors" python3 build/scripts/ai-repo-updater.py known-errors
    run_step "diff-summary" python3 build/scripts/ai-repo-updater.py diff-summary
fi

if [[ "$NODE_AVAILABLE" == true && -f package-lock.json ]]; then
    run_step "npm-ci" npm ci
fi

if [[ "$MAKE_AVAILABLE" == true && -f Makefile ]]; then
    grep -qE '^[[:space:]]*ai-audit:' Makefile 2>/dev/null && \
        run_step "ai-audit" make ai-audit
    grep -qE '^[[:space:]]*ai-docs-drift:' Makefile 2>/dev/null && \
        run_step "ai-docs-drift" make ai-docs-drift
    grep -qE '^[[:space:]]*verify-adrs:' Makefile 2>/dev/null && \
        run_step "verify-adrs" make verify-adrs
fi

STEPS_JSON="$(paste -sd, "$TMP_STEPS")"
rm -f "$TMP_STEPS"

cat >"$STATUS_FILE" <<EOF
{
  "timestamp": "$(date -u +"%Y-%m-%dT%H:%M:%SZ")",
  "mode": "light",
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
    "next_action": "Run full maintenance for src/tests/project changes."
  }
}
EOF

cat >"$SUMMARY_FILE" <<EOF
# Meridian Maintenance Status

- Mode: light
- Timestamp: $(date -u +"%Y-%m-%dT%H:%M:%SZ")
- Repo root: $ROOT_DIR
- dotnet available: $DOTNET_AVAILABLE
- node available: $NODE_AVAILABLE
- python available: $PYTHON_AVAILABLE
- make available: $MAKE_AVAILABLE

Artifacts:
- .ai/maintenance-status.json
EOF

log "Light maintenance complete"
