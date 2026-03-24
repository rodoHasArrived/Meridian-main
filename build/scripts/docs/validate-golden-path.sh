#!/usr/bin/env bash
# =============================================================================
# Golden Path Installer Reference Validator
# =============================================================================
#
# Checks that every installer script referenced in docs and the Makefile
# actually exists in the repository.  Fails with a non-zero exit code and
# a clear error summary if any reference is broken.
#
# Usage:
#   ./build/scripts/docs/validate-golden-path.sh
#
# =============================================================================

set -euo pipefail

# Move to repo root so relative paths work regardless of where the script is
# invoked from.
REPO_ROOT="$(git rev-parse --show-toplevel 2>/dev/null)" || {
    # Fall back to the directory two levels above this script.
    SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
    REPO_ROOT="$(cd "${SCRIPT_DIR}/../../.." && pwd)"
}
cd "$REPO_ROOT"

GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

pass()         { echo -e "  ${GREEN}OK${NC}             $1"; }
fail()         { echo -e "  ${RED}MISSING${NC}        $1"; FAILURES+=("$1"); }
fail_notexec() { echo -e "  ${RED}NOT EXECUTABLE${NC} $1"; FAILURES+=("$1"); }

FAILURES=()

# ---------------------------------------------------------------------------
# Golden-path files that MUST exist
# ---------------------------------------------------------------------------
#
# These are the installer scripts and supporting files referenced from:
#   - docs/HELP.md
#   - docs/getting-started/README.md
#   - Makefile (install, install-docker, install-native, check-deps,
#               install-hooks, publish, publish-linux, publish-windows,
#               publish-macos targets)
#
REQUIRED_FILES=(
    # Installer
    "build/scripts/install/install.sh"

    # Git hook installer
    "build/scripts/hooks/install-hooks.sh"

    # Publisher
    "build/scripts/publish/publish.sh"

    # Config template (setup-config target copies this)
    "config/appsettings.sample.json"

    # Docker compose (docker-up target uses this)
    "deploy/docker/docker-compose.yml"

    # Dockerfile (docker-build target uses this)
    "deploy/docker/Dockerfile"

    # Key documentation files linked from getting-started
    "docs/getting-started/README.md"
    "docs/HELP.md"
    "docs/providers/alpaca-setup.md"
    "docs/providers/interactive-brokers-setup.md"
    "docs/providers/provider-comparison.md"
    "docs/providers/data-sources.md"
    "docs/reference/environment-variables.md"
    "docs/providers/backfill-guide.md"
    "docs/architecture/storage-design.md"
    "docs/status/ROADMAP.md"
)

echo ""
echo -e "${BLUE}=== Golden Path Installer Reference Validation ===${NC}"
echo ""
echo "Checking that all installer-referenced files exist..."
echo ""

for f in "${REQUIRED_FILES[@]}"; do
    if [ -f "$f" ]; then
        pass "$f"
    else
        fail "$f"
    fi
done

# ---------------------------------------------------------------------------
# Executable check — installer scripts must be executable
# ---------------------------------------------------------------------------
REQUIRED_EXECUTABLES=(
    "build/scripts/install/install.sh"
    "build/scripts/hooks/install-hooks.sh"
    "build/scripts/publish/publish.sh"
)

echo ""
echo "Checking that installer scripts are executable..."
echo ""

for f in "${REQUIRED_EXECUTABLES[@]}"; do
    if [ ! -e "$f" ]; then
        # Already flagged as missing above; skip the executable check.
        continue
    fi
    if [ -x "$f" ]; then
        pass "$f (executable)"
    else
        fail_notexec "$f  (run: chmod +x $f)"
    fi
done

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------
echo ""
if [ "${#FAILURES[@]}" -eq 0 ]; then
    echo -e "${GREEN}All golden-path installer references are valid.${NC}"
    echo ""
    exit 0
else
    echo -e "${RED}Validation FAILED — ${#FAILURES[@]} issue(s):${NC}"
    for f in "${FAILURES[@]}"; do
        echo -e "  ${YELLOW}${f}${NC}"
    done
    echo ""
    echo -e "${YELLOW}Create the missing files or update the documentation that references them.${NC}"
    echo ""
    exit 1
fi
