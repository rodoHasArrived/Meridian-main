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

prerequisite_python() {
    local fallback=""
    local candidate
    for candidate in python3 python; do
        if have "$candidate"; then
            if [[ -z "$fallback" ]]; then
                fallback="$candidate"
            fi
            if "$candidate" "$ROOT_DIR/build/python/prerequisites.py" --python-version-ok >/dev/null 2>&1; then
                printf '%s\n' "$candidate"
                return 0
            fi
        fi
    done
    printf '%s\n' "$fallback"
}

# Derive the .NET install channel (major.minor) from global.json so this script
# always matches doctor and CI. The fallback allows .NET bootstrap when Python
# is unavailable; the shared prerequisite check must still pass before restore.
dotnet_channel_from_global_json() {
    local channel=""
    local python_cmd
    python_cmd="$(prerequisite_python)"
    if [[ -n "$python_cmd" && -f "$ROOT_DIR/build/python/prerequisites.py" ]]; then
        channel="$("$python_cmd" "$ROOT_DIR/build/python/prerequisites.py" --dotnet-channel 2>/dev/null || true)"
    fi
    printf '%s\n' "${channel:-10.0}"
}

ROOT_DIR="$(git rev-parse --show-toplevel 2>/dev/null || pwd)"
cd "$ROOT_DIR"

[[ -f Meridian.sln ]] || {
    err "Meridian.sln not found"
    exit 1
}
[[ -f README.md ]] || {
    err "README.md not found"
    exit 1
}

mkdir -p .ai data logs scripts/ai

ENV_FILE=".ai/env.sh"
STATUS_FILE=".ai/setup-status.json"

log "Repo root: $ROOT_DIR"

install_dotnet_if_missing() {
    if have dotnet; then
        return 0
    fi

    if ! have curl; then
        err "dotnet not found and curl is unavailable"
        exit 1
    fi

    local channel
    channel="$(dotnet_channel_from_global_json)"
    log "Installing .NET SDK ${channel}"
    curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
    bash /tmp/dotnet-install.sh --channel "$channel" --install-dir "$HOME/.dotnet"

    export DOTNET_ROOT="$HOME/.dotnet"
    export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"
    hash -r

    have dotnet || {
        err "dotnet installation failed"
        exit 1
    }
}

check_prerequisites() {
    local python_cmd
    python_cmd="$(prerequisite_python)"
    if [[ -z "$python_cmd" ]]; then
        err "Python is required to check setup prerequisites. Install Python from https://www.python.org/downloads/ and add python3 or python to PATH."
        return 1
    fi

    log "Checking shared prerequisites before installing packages or restoring"
    "$python_cmd" build/python/prerequisites.py --lane verify-fast --skip-python-packages
}

resolve_dotnet_root() {
    if [[ -n "${DOTNET_ROOT:-}" && -d "$DOTNET_ROOT/host/fxr" ]]; then
        printf '%s\n' "$DOTNET_ROOT"
        return 0
    fi

    if have dotnet; then
        local dotnet_path
        dotnet_path="$(command -v dotnet)"
        local dotnet_realpath
        dotnet_realpath="$(python3 -c 'import os, sys; print(os.path.realpath(sys.argv[1]))' "$dotnet_path" 2>/dev/null || readlink -f "$dotnet_path")"
        local dotnet_dir
        dotnet_dir="$(cd "$(dirname "$dotnet_realpath")" && pwd -P)"

        local parent_dir
        parent_dir="$(cd "$dotnet_dir/.." && pwd -P)"
        local candidate
        for candidate in "$dotnet_dir" "$parent_dir" "$HOME/.dotnet"; do
            if [[ -d "$candidate/host/fxr" ]]; then
                printf '%s\n' "$candidate"
                return 0
            fi
        done
    fi

    printf '%s\n' "$HOME/.dotnet"
}

write_env_file() {
    local dotnet_root
    dotnet_root="$(resolve_dotnet_root)"

    cat >"$ENV_FILE" <<EOF
#!/usr/bin/env bash
export DOTNET_ROOT="$dotnet_root"
export DOTNET_ROOT_X64="$dotnet_root"
export PATH="$dotnet_root:$dotnet_root/tools:\$PATH"
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_NOLOGO=true
export DOTNET_CLI_TELEMETRY_OPTOUT=1
EOF
    chmod +x "$ENV_FILE"
}

log "Initial tool versions"
git --version || true
python3 --version || true
node --version || true
npm --version || true
dotnet --version || true

install_dotnet_if_missing
check_prerequisites
write_env_file
# shellcheck disable=SC1090
source "$ENV_FILE"

log "Resolved tool versions"
dotnet --info || true

if [[ -f package-lock.json ]] && have npm; then
    log "Installing Node dependencies"
    npm ci
fi

if [[ -f config/appsettings.sample.json && ! -f config/appsettings.json ]]; then
    log "Creating local config from sample"
    cp config/appsettings.sample.json config/appsettings.json
fi

log "Restoring .NET dependencies"
dotnet restore Meridian.sln /p:EnableWindowsTargeting=true --verbosity minimal

if have python3 && [[ -f build/scripts/ai-repo-updater.py ]]; then
    log "Refreshing AI known-errors context"
    python3 build/scripts/ai-repo-updater.py known-errors || warn "known-errors step failed"
fi

cat >.ai/AGENTS_CONTEXT.md <<'EOF'
# Meridian agent context

Read first:
- README.md
- CLAUDE.md
- docs/ai/ai-known-errors.md

Preferred commands:
- bash scripts/ai/maintenance-light.sh
- bash scripts/ai/maintenance-full.sh
- bash scripts/ai/route-maintenance.sh
- make ai-audit
- make ai-verify

Rules:
- prefer small, targeted edits
- preserve CPM rules
- use CancellationToken on async methods
- do not add PackageReference Version attributes
EOF

cat >"$STATUS_FILE" <<EOF
{
  "timestamp": "$(date -u +"%Y-%m-%dT%H:%M:%SZ")",
  "repo_root": "$ROOT_DIR",
  "dotnet_available": true,
  "node_available": $(have node && have npm && echo true || echo false),
  "python_available": $(have python3 && echo true || echo false),
  "status": "ok"
}
EOF

log "Setup complete"
log "Run: source $ENV_FILE"
