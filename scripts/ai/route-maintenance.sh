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

# Parse arguments — supports both the legacy positional style and the newer
# named-flag style used by the CI workflow:
#   route-maintenance.sh [--classify-only] [--base <ref>] [--head <sha>]
#   route-maintenance.sh <base-ref>   (legacy positional form)
CLASSIFY_ONLY=false
BASE_REF="origin/main"
HEAD_REF="HEAD"

while [[ $# -gt 0 ]]; do
    case "$1" in
        --classify-only)
            CLASSIFY_ONLY=true
            shift
            ;;
        --base)
            if [[ -z "${2:-}" || "${2:-}" == --* ]]; then
                warn "--base requires a non-empty argument"
                exit 1
            fi
            BASE_REF="$2"
            shift 2
            ;;
        --head)
            if [[ -z "${2:-}" || "${2:-}" == --* ]]; then
                warn "--head requires a non-empty argument"
                exit 1
            fi
            HEAD_REF="$2"
            shift 2
            ;;
        *)
            # Legacy: first positional argument is the base ref
            BASE_REF="$1"
            shift
            ;;
    esac
done

if ! git rev-parse --verify "$BASE_REF" >/dev/null 2>&1; then
    warn "Base ref '$BASE_REF' not found; defaulting to HEAD~1"
    BASE_REF="HEAD~1"
fi

CHANGED_FILES="$(git diff --name-only "$BASE_REF"..."$HEAD_REF" 2>/dev/null || true)"
printf '%s\n' "$CHANGED_FILES" >.ai/changed-files.txt

# ── Determine routing mode ───────────────────────────────────────────────────
MODE="light"
DOCS_ONLY=false
WORKFLOW_CHANGES=false
UI_CHANGES=false
LEDGER_CHANGES=false

if [[ -z "$CHANGED_FILES" ]]; then
    log "No changed files detected; routing to light maintenance"
elif echo "$CHANGED_FILES" | grep -Eq '^(src/|tests/)' || \
     echo "$CHANGED_FILES" | grep -Eq '\.(csproj|fsproj)$' || \
     echo "$CHANGED_FILES" | grep -Eq '^(Directory\.Packages\.props|global\.json)$'; then
    log "Detected code/project changes -> full maintenance"
    MODE="full"
elif echo "$CHANGED_FILES" | grep -Eq '^(\.github/workflows/|scripts/|build/|Makefile$)'; then
    log "Detected workflow/script changes -> full maintenance"
    MODE="full"
elif echo "$CHANGED_FILES" | grep -Eq '^(docs/|CLAUDE\.md$)'; then
    log "Detected docs/AI-doc changes -> light maintenance"
else
    log "Defaulting to light maintenance"
fi

# ── Detect specific change categories (used by downstream jobs) ──────────────
if echo "$CHANGED_FILES" | grep -Eq '^\.github/workflows/'; then
    WORKFLOW_CHANGES=true
fi
if echo "$CHANGED_FILES" | grep -Eq '^src/Meridian\.(Ui|Wpf)/'; then
    UI_CHANGES=true
fi
if echo "$CHANGED_FILES" | grep -Eq '^(src/Meridian\.(Ledger|FSharp\.Ledger|FSharp\.DirectLending|Backtesting)|tests/Meridian\.(Backtesting|DirectLending))'; then
    LEDGER_CHANGES=true
fi
# docs_only is true whenever any docs files were touched (regardless of mode)
if echo "$CHANGED_FILES" | grep -Eq '^(docs/|CLAUDE\.md$)'; then
    DOCS_ONLY=true
fi

# Count non-empty lines safely
CHANGED_COUNT=0
if [[ -n "$CHANGED_FILES" ]]; then
    CHANGED_COUNT="$(echo "$CHANGED_FILES" | grep -c . || true)"
fi

# ── Write route JSON consumed by the CI workflow ─────────────────────────────
cat >.ai/maintenance-route.json <<EOF
{
  "mode": "$MODE",
  "docs_only": $DOCS_ONLY,
  "workflow_changes": $WORKFLOW_CHANGES,
  "ui_changes": $UI_CHANGES,
  "ledger_changes": $LEDGER_CHANGES,
  "base_ref": "$BASE_REF",
  "head_ref": "$HEAD_REF",
  "changed_files_count": $CHANGED_COUNT
}
EOF

if [[ "$CLASSIFY_ONLY" == true ]]; then
    log "Classification complete (classify-only mode); mode=$MODE"
    exit 0
fi

# ── Execute appropriate maintenance script ───────────────────────────────────
if [[ "$MODE" == "full" ]]; then
    exec bash scripts/ai/maintenance-full.sh
else
    exec bash scripts/ai/maintenance-light.sh
fi
