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
#
# =============================================================================

set -euo pipefail

# --- colours -----------------------------------------------------------------
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
RED='\033[0;31m'
NC='\033[0m'

MODE="${1:-}"

# --- helpers -----------------------------------------------------------------
info()    { echo -e "${BLUE}$*${NC}"; }
success() { echo -e "${GREEN}$*${NC}"; }
warn()    { echo -e "${YELLOW}WARNING: $*${NC}"; }
error()   { echo -e "${RED}ERROR: $*${NC}" >&2; exit 1; }

check_dotnet() {
    if ! command -v dotnet >/dev/null 2>&1; then
        error ".NET SDK not found. Install from https://dot.net/download"
    fi
    local ver
    ver=$(dotnet --version)
    info "  .NET SDK ${ver} found"
    if [[ "${ver%%.*}" -lt 9 ]]; then
        error ".NET SDK 9.0.100 or later is required by global.json (found ${ver}). Please install .NET 9 from https://dot.net/download"
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
    info "  2. Start the web dashboard:"
    info "       make run-ui"
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
    info "  Open dashboard:      http://localhost:8080"
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
        if [[ "${ver%%.*}" -lt 9 ]]; then
            warn "  .NET 9+ recommended"
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

interactive() {
    info ""
    info "╔══════════════════════════════════════════════╗"
    info "║         Meridian Installer                   ║"
    info "╚══════════════════════════════════════════════╝"
    info ""
    info "How would you like to install Meridian?"
    info ""
    info "  1) Docker   — recommended, easiest setup"
    info "  2) Native   — requires .NET 9 SDK"
    info "  3) Check    — verify prerequisites only"
    info "  q) Quit"
    info ""
    read -r -p "Choice [1/2/3/q]: " choice
    case "$choice" in
        1|docker)  install_docker  ;;
        2|native)  install_native  ;;
        3|check)   check_prerequisites ;;
        q|Q)       info "Aborted."; exit 0 ;;
        *)         error "Invalid choice: $choice" ;;
    esac
}

# --- main --------------------------------------------------------------------
case "$MODE" in
    --docker)  install_docker ;;
    --native)  install_native ;;
    --check)   check_prerequisites ;;
    "")        interactive ;;
    *)         error "Unknown option: $MODE" ;;
esac
