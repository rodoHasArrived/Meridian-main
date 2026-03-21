#!/usr/bin/env bash
set -Eeuo pipefail

log() {
    printf '\n[cleanup] %s\n' "$*"
}

warn() {
    printf '\n[cleanup:warn] %s\n' "$*" >&2
}

err() {
    printf '\n[cleanup:error] %s\n' "$*" >&2
}

have() {
    command -v "$1" >/dev/null 2>&1
}

on_error() {
    local exit_code=$?
    local line_no="${1:-unknown}"
    err "Failed at line ${line_no} with exit code ${exit_code}"
    exit "$exit_code"
}
trap 'on_error $LINENO' ERR

ROOT_DIR="$(git rev-parse --show-toplevel 2>/dev/null || pwd)"
cd "$ROOT_DIR"

STRICT=0
CLEAN_DOTNET=1
CLEAN_NODE=1
CLEAN_LOGS=1
CLEAN_AI=1
CLEAN_TEST_RESULTS=1
CLEAN_TEMP=1

usage() {
    cat <<EOF
Usage:
  bash scripts/ai/cleanup.sh [options]

Options:
  --strict               Fail on non-essential cleanup failures
  --skip-dotnet          Skip dotnet clean
  --skip-node            Skip node_modules / npm cleanup
  --skip-logs            Skip log cleanup
  --skip-ai              Skip .ai cleanup
  --skip-test-results    Skip test result cleanup
  --skip-temp            Skip temp file cleanup
  -h, --help             Show this help
EOF
}

run_or_warn() {
    local description="$1"
    shift

    log "$description"
    if ! "$@"; then
        if [[ "$STRICT" -eq 1 ]]; then
            err "Failed: $description"
            exit 1
        fi
        warn "Failed: $description"
    fi
}

parse_args() {
    while [[ $# -gt 0 ]]; do
        case "$1" in
            --strict) STRICT=1 ;;
            --skip-dotnet) CLEAN_DOTNET=0 ;;
            --skip-node) CLEAN_NODE=0 ;;
            --skip-logs) CLEAN_LOGS=0 ;;
            --skip-ai) CLEAN_AI=0 ;;
            --skip-test-results) CLEAN_TEST_RESULTS=0 ;;
            --skip-temp) CLEAN_TEMP=0 ;;
            -h|--help)
                usage
                exit 0
                ;;
            *)
                err "Unknown argument: $1"
                usage
                exit 2
                ;;
        esac
        shift
    done
}

require_repo_files() {
    [[ -f Meridian.sln ]] || { err "Meridian.sln not found"; exit 1; }
}

cleanup_dotnet() {
    if ! have dotnet; then
        warn "dotnet is unavailable; skipping dotnet clean"
        return 0
    fi

    run_or_warn "Clean .NET build artifacts" dotnet clean Meridian.sln --verbosity quiet

    log "Removing common .NET output folders"
    find . \
        \( -type d -name bin -o -type d -name obj \) \
        -not -path "./.git/*" \
        -prune -exec rm -rf {} +
}

cleanup_node() {
    if [[ ! -f package.json ]]; then
        return 0
    fi

    if [[ -d node_modules ]]; then
        run_or_warn "Remove node_modules" rm -rf node_modules
    fi

    rm -f npm-debug.log yarn-debug.log yarn-error.log pnpm-debug.log 2>/dev/null || true
}

cleanup_logs() {
    [[ -d logs ]] || return 0

    run_or_warn "Remove log files" find logs -type f -delete
}

cleanup_ai() {
    [[ -d .ai ]] || return 0

    run_or_warn "Remove AI status/report files" find .ai -maxdepth 1 -type f \
        \( -name "*.md" -o -name "*.json" -o -name "*.log" -o -name "*.tmp" \) \
        -delete
}

cleanup_test_results() {
    rm -rf TestResults test-results artifacts/test-results coverage .coverage 2>/dev/null || true
}

cleanup_temp() {
    rm -f msbuild.binlog 2>/dev/null || true

    find . \
        -type f \
        \( -name "*.tmp" -o -name "*.temp" -o -name "*.bak" \) \
        -not -path "./.git/*" \
        -delete 2>/dev/null || true
}

write_status_file() {
    mkdir -p .ai

    cat > .ai/CLEANUP_STATUS.md <<EOF
# Meridian Cleanup Status

Generated: $(date -u +"%Y-%m-%dT%H:%M:%SZ")
Repo root: $ROOT_DIR

## Flags used
- strict: $STRICT
- clean_dotnet: $CLEAN_DOTNET
- clean_node: $CLEAN_NODE
- clean_logs: $CLEAN_LOGS
- clean_ai: $CLEAN_AI
- clean_test_results: $CLEAN_TEST_RESULTS
- clean_temp: $CLEAN_TEMP
EOF
}

main() {
    parse_args "$@"
    require_repo_files

    log "Repo root: $ROOT_DIR"

    if [[ "$CLEAN_DOTNET" -eq 1 ]]; then
        cleanup_dotnet
    fi

    if [[ "$CLEAN_NODE" -eq 1 ]]; then
        cleanup_node
    fi

    if [[ "$CLEAN_LOGS" -eq 1 ]]; then
        cleanup_logs
    fi

    if [[ "$CLEAN_AI" -eq 1 ]]; then
        cleanup_ai
    fi

    if [[ "$CLEAN_TEST_RESULTS" -eq 1 ]]; then
        run_or_warn "Remove test result artifacts" cleanup_test_results
    fi

    if [[ "$CLEAN_TEMP" -eq 1 ]]; then
        run_or_warn "Remove temporary files" cleanup_temp
    fi

    write_status_file
    log "Cleanup complete"
}

main "$@"
