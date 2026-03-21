#!/usr/bin/env bash
set -Eeuo pipefail

log() {
    printf '\n[setup] %s\n' "$*"
}

warn() {
    printf '\n[setup:warn] %s\n' "$*" >&2
}

err() {
    printf '\n[setup:error] %s\n' "$*" >&2
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
INSTALL_DOTNET=1
RUN_RESTORE=1
RUN_NODE_INSTALL=1
RUN_AI_HELPER=1
WRITE_CONTEXT=1
COPY_SAMPLE_CONFIG=1
DOTNET_CHANNEL="9.0"

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

usage() {
    cat <<EOF
Usage:
  bash scripts/ai/setup.sh [options]

Options:
  --strict             Fail on non-essential step failures
  --install-dotnet     Install .NET SDK if dotnet is missing (default: enabled)
  --no-install-dotnet  Do not install .NET SDK automatically
  --dotnet-channel X   Install a specific .NET channel (default: ${DOTNET_CHANNEL})
  --skip-restore       Skip dotnet restore
  --skip-node          Skip npm install
  --skip-ai-helper     Skip build/scripts/ai-repo-updater.py
  --skip-context       Skip writing .ai/AGENTS_CONTEXT.md
  --skip-config        Skip copying config/appsettings.sample.json to config/appsettings.json
  -h, --help           Show this help
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
            --install-dotnet) INSTALL_DOTNET=1 ;;
            --no-install-dotnet) INSTALL_DOTNET=0 ;;
            --dotnet-channel)
                shift
                [[ $# -gt 0 ]] || { err "--dotnet-channel requires a value"; exit 2; }
                DOTNET_CHANNEL="$1"
                ;;
            --skip-restore) RUN_RESTORE=0 ;;
            --skip-node) RUN_NODE_INSTALL=0 ;;
            --skip-ai-helper) RUN_AI_HELPER=0 ;;
            --skip-context) WRITE_CONTEXT=0 ;;
            --skip-config) COPY_SAMPLE_CONFIG=0 ;;
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
    [[ -f README.md ]] || { err "README.md not found"; exit 1; }
}

install_dotnet_sdk() {
    if have dotnet; then
        return 0
    fi

    if [[ "$INSTALL_DOTNET" -ne 1 ]]; then
        warn "dotnet is not installed and automatic installation is disabled"
        return 1
    fi

    if ! have curl; then
        err "curl is unavailable; cannot install dotnet automatically"
        return 1
    fi

    local tmp_script=""
    tmp_script="$(mktemp)"

    cleanup_tmp_script() {
        [[ -n "$tmp_script" && -f "$tmp_script" ]] && rm -f "$tmp_script"
    }

    log "Installing .NET SDK ${DOTNET_CHANNEL}"

    if ! curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$tmp_script"; then
        cleanup_tmp_script
        err "Failed to download dotnet-install.sh"
        return 1
    fi

    if ! bash "$tmp_script" --channel "$DOTNET_CHANNEL" --install-dir "$HOME/.dotnet"; then
        cleanup_tmp_script
        err "Failed to install .NET SDK ${DOTNET_CHANNEL}"
        return 1
    fi

    cleanup_tmp_script

    export DOTNET_ROOT="$HOME/.dotnet"
    export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"
    hash -r

    if ! have dotnet; then
        err "dotnet installation completed but dotnet is still unavailable in PATH"
        return 1
    fi

    log "Installed dotnet: $(dotnet --version)"
}

ensure_dotnet() {
    if have dotnet; then
        return 0
    fi

    install_dotnet_sdk

    if ! have dotnet; then
        err "dotnet is required for this repository"
        return 1
    fi
}

npm_ci_quiet() {
    npm ci --no-fund --no-audit
}

write_env_file() {
    mkdir -p .ai

    cat > .ai/env.sh <<'EOF'
#!/usr/bin/env bash
export DOTNET_ROOT="${HOME}/.dotnet"
export PATH="${DOTNET_ROOT}:${DOTNET_ROOT}/tools:${PATH}"
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_NOLOGO=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1
EOF

    chmod +x .ai/env.sh
}

write_agents_context() {
    mkdir -p .ai

    cat > .ai/AGENTS_CONTEXT.md <<'EOF'
# Meridian Codex Context

Read first:
- README.md
- CLAUDE.md
- docs/ai/ai-known-errors.md

Suggested commands:
- bash scripts/ai/setup.sh
- bash scripts/ai/maintenance.sh --light
- bash scripts/ai/maintenance.sh --full
- make build
- make test
- make ai-verify

Notes:
- Meridian is a .NET repository.
- Prefer small, targeted edits.
- Preserve existing project boundaries and central package management.
- Prefer repository scripts and Make targets over ad-hoc commands when available.
EOF
}

copy_sample_config() {
    if [[ "$COPY_SAMPLE_CONFIG" -eq 1 ]] && [[ -f config/appsettings.sample.json ]] && [[ ! -f config/appsettings.json ]]; then
        log "Creating local config from sample"
        cp config/appsettings.sample.json config/appsettings.json
    fi
}

write_status_file() {
    mkdir -p .ai

    cat > .ai/SETUP_STATUS.md <<EOF
# Meridian Setup Status

Generated: $(date -u +"%Y-%m-%dT%H:%M:%SZ")
Repo root: $ROOT_DIR

## Tool availability
- git: $(have git && echo yes || echo no)
- python3: $(have python3 && echo yes || echo no)
- node: $(have node && echo yes || echo no)
- npm: $(have npm && echo yes || echo no)
- dotnet: $(have dotnet && echo yes || echo no)
- make: $(have make && echo yes || echo no)

## Versions
- python3: $(python3 --version 2>/dev/null || echo unavailable)
- node: $(node --version 2>/dev/null || echo unavailable)
- npm: $(npm --version 2>/dev/null || echo unavailable)
- dotnet: $(dotnet --version 2>/dev/null || echo unavailable)
- make: $(make --version 2>/dev/null | head -n 1 || echo unavailable)

## Flags used
- strict: $STRICT
- install_dotnet: $INSTALL_DOTNET
- run_restore: $RUN_RESTORE
- run_node_install: $RUN_NODE_INSTALL
- run_ai_helper: $RUN_AI_HELPER
- write_context: $WRITE_CONTEXT
- copy_sample_config: $COPY_SAMPLE_CONFIG
- dotnet_channel: $DOTNET_CHANNEL

## Suggested next commands
- bash scripts/ai/maintenance.sh --light
- bash scripts/ai/maintenance.sh --full
- bash scripts/ai/route-maintenance.sh
EOF
}

show_versions() {
    log "Tool versions"
    git --version || true
    python3 --version || true
    node --version || true
    npm --version || true
    dotnet --version || true
    make --version 2>/dev/null | head -n 1 || true
}

main() {
    parse_args "$@"

    log "Repo root: $ROOT_DIR"
    require_repo_files
    mkdir -p .ai data logs

    show_versions
    ensure_dotnet
    write_env_file
    copy_sample_config

    log "Resolved tool versions"
    dotnet --info || true

    if [[ "$RUN_NODE_INSTALL" -eq 1 ]]; then
        if [[ -f package-lock.json ]] && have npm; then
            run_or_warn "Install Node dependencies" npm_ci_quiet
        elif [[ -f package.json ]]; then
            warn "package.json exists, but npm is unavailable; skipping Node dependency installation"
        fi
    fi

    if [[ "$RUN_RESTORE" -eq 1 ]]; then
        run_or_warn "Restore .NET dependencies" dotnet restore Meridian.sln /p:EnableWindowsTargeting=true
    fi

    if [[ "$RUN_AI_HELPER" -eq 1 ]]; then
        if have python3 && [[ -f build/scripts/ai-repo-updater.py ]]; then
            run_or_warn "Run repo AI helper (known-errors)" python3 build/scripts/ai-repo-updater.py known-errors
        fi
    fi

    if [[ "$WRITE_CONTEXT" -eq 1 ]]; then
        log "Writing AI agent context"
        write_agents_context
    fi

    write_status_file
    log "Setup complete"
    log "Environment file: .ai/env.sh"
}

main "$@"
