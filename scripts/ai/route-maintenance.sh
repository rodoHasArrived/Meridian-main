#!/usr/bin/env bash
set -Eeuo pipefail

log() {
    printf '\n[route] %s\n' "$*"
}

warn() {
    printf '\n[route:warn] %s\n' "$*" >&2
}

ROOT_DIR="$(git rev-parse --show-toplevel 2>/dev/null || pwd)"
cd "$ROOT_DIR"
mkdir -p .ai

BASE_REF="${1:-origin/main}"

if ! git rev-parse --verify "$BASE_REF" >/dev/null 2>&1; then
    warn "Base ref '$BASE_REF' not found; defaulting to HEAD~1"
    BASE_REF="HEAD~1"
fi

CHANGED_FILES="$(git diff --name-only "$BASE_REF"...HEAD || true)"
printf '%s\n' "$CHANGED_FILES" >.ai/changed-files.txt

if [[ -z "$CHANGED_FILES" ]]; then
    log "No changed files detected; running light maintenance"
    exec bash scripts/ai/maintenance-light.sh
fi

if echo "$CHANGED_FILES" | grep -Eq '^(src/|tests/|.*\.csproj$|Directory\.Packages\.props$|global\.json$)'; then
    log "Detected code/project changes -> full maintenance"
    exec bash scripts/ai/maintenance-full.sh
fi

if echo "$CHANGED_FILES" | grep -Eq '^(\.github/workflows/|scripts/|build/|Makefile$)'; then
    log "Detected workflow/script changes -> full maintenance"
    exec bash scripts/ai/maintenance-full.sh
fi

if echo "$CHANGED_FILES" | grep -Eq '^(docs/|CLAUDE\.md$|docs/ai/)'; then
    log "Detected docs/AI-doc changes -> light maintenance"
    exec bash scripts/ai/maintenance-light.sh
fi

log "Defaulting to light maintenance"
exec bash scripts/ai/maintenance-light.sh
