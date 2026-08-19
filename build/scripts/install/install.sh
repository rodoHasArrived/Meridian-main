#!/usr/bin/env bash
# =============================================================================
# Meridian Installer
# =============================================================================
#
# Usage:
#   ./build/scripts/install/install.sh            Interactive (Docker or Native)
#   ./build/scripts/install/install.sh --docker   Docker-based installation
#   ./build/scripts/install/install.sh --native   Native .NET installation
#   ./build/scripts/install/install.sh --check    Check prerequisites only
#   ./build/scripts/install/install.sh --agent-tools  Install cloud-agent toolchain
#   ./build/scripts/install/install.sh --help     Show help
#
# =============================================================================

set -euo pipefail

# --- colours -----------------------------------------------------------------
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
RED='\033[0;31m'
NC='\033[0m'

MODE=""

# Move to repo root so all repo-relative paths (config/, data/, Meridian.sln,
# deploy/) resolve correctly regardless of where the script is invoked from.
REPO_ROOT="$(git rev-parse --show-toplevel 2>/dev/null)" || {
    SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
    REPO_ROOT="$(cd "${SCRIPT_DIR}/../../.." && pwd)"
}
cd "$REPO_ROOT"

# --- helpers -----------------------------------------------------------------
info()    { echo -e "${BLUE}$*${NC}"; }
success() { echo -e "${GREEN}$*${NC}"; }
warn()    { echo -e "${YELLOW}WARNING: $*${NC}"; }
error()   { echo -e "${RED}ERROR: $*${NC}" >&2; exit 1; }

print_help() {
    cat <<'EOF'
Usage:
  ./build/scripts/install/install.sh
  ./build/scripts/install/install.sh --docker
  ./build/scripts/install/install.sh --native
  ./build/scripts/install/install.sh --check
  ./build/scripts/install/install.sh --agent-tools
  ./build/scripts/install/install.sh --help
EOF
}

run_as_root() {
    if [ "$(id -u)" -eq 0 ]; then
        "$@"
    elif command -v sudo >/dev/null 2>&1; then
        sudo "$@"
    else
        error "This step requires root privileges; install sudo or run as root."
    fi
}

dotnet_major_version() {
    local ver
    ver=$(dotnet --version)
    echo "${ver%%.*}"
}

install_dotnet_sdk_10() {
    if command -v dotnet >/dev/null 2>&1 && [ "$(dotnet_major_version)" -ge 10 ]; then
        info "  .NET SDK $(dotnet --version) already available"
        return
    fi

    info "  Installing .NET SDK channel 10.0..."
    local install_dir="${HOME}/.dotnet"
    mkdir -p "$install_dir"
    local installer
    installer="$(mktemp)"
    curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$installer"
    bash "$installer" --channel 10.0 --install-dir "$install_dir"
    rm -f "$installer"
    export DOTNET_ROOT="$install_dir"
    export PATH="$install_dir:$PATH"
    success "  .NET SDK $(dotnet --version) ready"
}

install_nodejs_20() {
    if command -v node >/dev/null 2>&1 && [ "$(node -v | sed 's/^v//' | cut -d. -f1)" -ge 20 ]; then
        info "  Node.js $(node -v) already available"
        return
    fi

    info "  Installing Node.js 20 LTS..."
    run_as_root apt-get update
    run_as_root apt-get install -y ca-certificates curl gnupg
    curl -fsSL https://deb.nodesource.com/setup_20.x | run_as_root bash -
    run_as_root apt-get install -y nodejs
    success "  Node.js $(node -v) ready"
}

install_python_312() {
    local python_minor=""
    if command -v python3 >/dev/null 2>&1; then
        python_minor="$(python3 -c 'import sys; print(f"{sys.version_info[0]}.{sys.version_info[1]}")')"
    fi

    if [ "$python_minor" = "3.12" ]; then
        info "  Python $(python3 --version 2>&1) already available"
    else
        info "  Installing Python 3.12..."
        run_as_root apt-get update
        run_as_root apt-get install -y python3.12 python3.12-venv python3-pip
        if ! command -v python3 >/dev/null 2>&1 && command -v python3.12 >/dev/null 2>&1; then
            run_as_root ln -sf "$(command -v python3.12)" /usr/local/bin/python3
        fi
        success "  Python $(python3 --version 2>&1) ready"
    fi

    # There is no requirements.txt at the repository root, so this used to report "skipping"
    # on every run and silently install nothing. The real file is the docs-automation one.
    local python_requirements="build/scripts/docs/requirements.txt"
    if [ -f "$python_requirements" ]; then
        info "  Installing Python dependencies from $python_requirements..."
        python3 -m pip install --upgrade pip
        python3 -m pip install -r "$python_requirements"
        success "  Python dependencies installed"
    else
        error "  Expected $python_requirements is missing; run this from the repository root."
    fi
}

install_powershell_7() {
    if command -v pwsh >/dev/null 2>&1; then
        info "  PowerShell $(pwsh --version) already available"
        return
    fi

    info "  Installing PowerShell 7..."
    run_as_root apt-get update
    run_as_root apt-get install -y wget apt-transport-https software-properties-common
    local os_version
    os_version="$(. /etc/os-release && echo "$VERSION_ID")"
    wget -q "https://packages.microsoft.com/config/ubuntu/${os_version}/packages-microsoft-prod.deb" -O /tmp/packages-microsoft-prod.deb
    run_as_root dpkg -i /tmp/packages-microsoft-prod.deb
    run_as_root apt-get update
    run_as_root apt-get install -y powershell
    success "  PowerShell $(pwsh --version) ready"
}

warm_meridian_restore() {
    info "  Running solution restore to warm NuGet cache..."
    dotnet restore Meridian.sln /p:EnableWindowsTargeting=true --verbosity minimal
    success "  NuGet restore cache warmed"
}

check_dotnet() {
    if ! command -v dotnet >/dev/null 2>&1; then
        error ".NET SDK not found. Install from https://dot.net/download"
    fi
    local ver
    ver=$(dotnet --version)
    info "  .NET SDK ${ver} found"
    if [[ "${ver%%.*}" -lt 10 ]]; then
        error ".NET SDK 10.0.100 or later is required by global.json (found ${ver}). Please install .NET 10 from https://dot.net/download"
    fi
}

check_docker() {
    if ! command -v docker >/dev/null 2>&1; then
        error "Docker not found. Install from https://docs.docker.com/get-docker/"
    fi
    info "  Docker $(docker --version | cut -d' ' -f3 | tr -d ',') found"
}

setup_config() {
    if [ ! -f config/appsettings.json ]; then
        cp config/appsettings.sample.json config/appsettings.json
        success "  Created config/appsettings.json from template"
        warn "  Edit config/appsettings.json and set your API credentials"
    else
        info "  config/appsettings.json already exists"
    fi
    mkdir -p data logs
}

install_native() {
    info ""
    info "=== Native .NET Installation ==="
    info ""

    info "[1/4] Checking prerequisites..."
    check_dotnet
    info ""

    info "[2/4] Setting up configuration..."
    setup_config
    info ""

    info "[3/4] Restoring packages..."
    dotnet restore Meridian.sln /p:EnableWindowsTargeting=true --verbosity quiet
    success "  Packages restored"
    info ""

    info "[4/4] Building..."
    dotnet build Meridian.sln -c Release --verbosity quiet --nologo /p:EnableWindowsTargeting=true
    success "  Build succeeded"
    info ""

    success "Native installation complete!"
    info ""
    info "Next steps:"
    info "  1. Set API credentials as environment variables:"
    info "       export ALPACA__KEYID=your-key-id"
    info "       export ALPACA__SECRETKEY=your-secret-key"
    info "  2. Start the desktop-local backend:"
    info "       make run"
}

install_docker() {
    info ""
    info "=== Docker Installation ==="
    info ""

    info "[1/3] Checking prerequisites..."
    check_docker
    info ""

    info "[2/3] Setting up configuration..."
    setup_config
    info ""

    info "[3/3] Building Docker image..."
    docker build -f deploy/docker/Dockerfile -t meridian:latest .
    success "  Docker image built"
    info ""

    success "Docker installation complete!"
    info ""
    info "Next steps:"
    info "  Start the container: docker compose -f deploy/docker/docker-compose.yml up -d"
    info "  API endpoint:        http://localhost:8080"
}

check_prerequisites() {
    info ""
    info "=== Prerequisite Check ==="
    local ok=true

    info ""
    info "Checking .NET SDK..."
    if command -v dotnet >/dev/null 2>&1; then
        local ver
        ver=$(dotnet --version)
        success "  .NET SDK ${ver}"
        if [[ "${ver%%.*}" -lt 10 ]]; then
            warn "  .NET SDK ${ver} is too old — 10.0.100 or later is required (see global.json)"
            ok=false
        fi
    else
        warn "  .NET SDK not found (required for native install)"
        ok=false
    fi

    info ""
    info "Checking Docker..."
    if command -v docker >/dev/null 2>&1; then
        success "  Docker $(docker --version | cut -d' ' -f3 | tr -d ',')"
    else
        warn "  Docker not found (required for Docker install)"
    fi

    info ""
    info "Checking config..."
    if [ -f config/appsettings.json ]; then
        success "  config/appsettings.json exists"
    else
        warn "  config/appsettings.json missing — run 'make setup-config'"
    fi

    info ""
    if [ "$ok" = "true" ]; then
        success "Prerequisite check passed"
    else
        warn "Some prerequisites are missing — see above"
        exit 1
    fi
}

install_agent_toolchain() {
    info ""
    info "=== Agent Toolchain Installation ==="
    info ""

    install_dotnet_sdk_10
    install_nodejs_20
    install_python_312
    install_powershell_7
    warm_meridian_restore

    info ""
    success "Agent toolchain setup complete."
    info "  dotnet:  $(dotnet --version)"
    info "  node:    $(node --version)"
    info "  python3: $(python3 --version 2>&1)"
    info "  pwsh:    $(pwsh --version)"
}

interactive() {
    info ""
    info "╔══════════════════════════════════════════════╗"
    info "║         Meridian Installer                   ║"
    info "╚══════════════════════════════════════════════╝"
    info ""
    info "How would you like to install Meridian?"
    info ""
    info "  1) Native   — recommended; requires the .NET SDK pinned by global.json"
    info "  2) Docker   — experimental, outside the supported envelope (see docs/adr/019)"
    info "  3) Check    — verify prerequisites only"
    info "  q) Quit"
    info ""
    read -r -p "Choice [1/2/3/q]: " choice
    case "$choice" in
        1|native)  install_native  ;;
        2|docker)  install_docker  ;;
        3|check)   check_prerequisites ;;
        q|Q)       info "Aborted."; exit 0 ;;
        *)         error "Invalid choice: $choice" ;;
    esac
}

# --- main --------------------------------------------------------------------
while [[ $# -gt 0 ]]; do
    case "$1" in
        --docker|--native|--check|--agent-tools|--help|-h)
            if [ -n "$MODE" ]; then
                error "Only one mode can be specified"
            fi
            MODE="$1"
            shift
            ;;
        *)
            error "Unknown option: $1"
            ;;
    esac
done

case "$MODE" in
    --docker)       install_docker ;;
    --native)       install_native ;;
    --check)        check_prerequisites ;;
    --agent-tools)  install_agent_toolchain ;;
    --help|-h)      print_help ;;
    "")             interactive ;;
    *)              error "Unknown option: $MODE" ;;
esac
