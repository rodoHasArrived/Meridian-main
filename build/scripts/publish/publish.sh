#!/usr/bin/env bash
# =============================================================================
# Meridian Publisher
# =============================================================================
#
# Publishes self-contained executables for one or all target platforms.
#
# Usage:
#   ./build/scripts/publish/publish.sh              All platforms
#   ./build/scripts/publish/publish.sh linux-x64    Linux x64 only
#   ./build/scripts/publish/publish.sh win-x64      Windows x64 only
#   ./build/scripts/publish/publish.sh osx-x64      macOS x64 only
#
# =============================================================================

set -euo pipefail

GREEN='\033[0;32m'
BLUE='\033[0;34m'
NC='\033[0m'

PROJECT="src/Meridian/Meridian.csproj"
OUTPUT_DIR="publish"
CONFIGURATION="Release"

info()    { echo -e "${BLUE}$*${NC}"; }
success() { echo -e "${GREEN}$*${NC}"; }

publish_rid() {
    local rid="$1"
    info "Publishing for ${rid}..."
    dotnet publish "$PROJECT" \
        -c "$CONFIGURATION" \
        -r "$rid" \
        --self-contained true \
        -p:PublishSingleFile=true \
        -p:EnableWindowsTargeting=true \
        --output "${OUTPUT_DIR}/${rid}" \
        --verbosity quiet \
        --nologo
    success "  Published to ${OUTPUT_DIR}/${rid}"
}

TARGET="${1:-}"

case "$TARGET" in
    linux-x64|win-x64|osx-x64|osx-arm64|linux-arm64)
        publish_rid "$TARGET"
        ;;
    "")
        info "Publishing for all platforms..."
        publish_rid "linux-x64"
        publish_rid "win-x64"
        publish_rid "osx-x64"
        ;;
    *)
        echo "ERROR: Unknown target '${TARGET}'. Valid: linux-x64, win-x64, osx-x64" >&2
        exit 1
        ;;
esac

success "Done. Artifacts in ${OUTPUT_DIR}/"
