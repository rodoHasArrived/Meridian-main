#!/usr/bin/env bash
# =============================================================================
# Meridian Git Hook Installer
# =============================================================================
#
# Copies pre-commit and commit-msg hooks from .githooks/ into .git/hooks/.
#
# Usage:
#   ./build/scripts/hooks/install-hooks.sh
#
# =============================================================================

set -euo pipefail

GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m'

REPO_ROOT="$(git rev-parse --show-toplevel 2>/dev/null)" || {
    echo -e "${RED}ERROR: Not inside a git repository.${NC}" >&2
    exit 1
}

HOOKS_SRC="${REPO_ROOT}/.githooks"
HOOKS_DST="${REPO_ROOT}/.git/hooks"

if [ ! -d "$HOOKS_SRC" ]; then
    echo -e "${RED}ERROR: .githooks/ directory not found at ${HOOKS_SRC}${NC}" >&2
    exit 1
fi

mkdir -p "$HOOKS_DST"

installed=0
for hook in "$HOOKS_SRC"/*; do
    name="$(basename "$hook")"
    dst="${HOOKS_DST}/${name}"
    cp "$hook" "$dst"
    chmod +x "$dst"
    echo -e "${GREEN}  Installed hook: ${name}${NC}"
    installed=$((installed + 1))
done

if [ "$installed" -eq 0 ]; then
    echo -e "${YELLOW}No hooks found in .githooks/ — nothing installed.${NC}"
else
    echo -e "${GREEN}${installed} hook(s) installed successfully.${NC}"
fi
