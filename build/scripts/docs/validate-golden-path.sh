#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
cd "$ROOT_DIR"

required_files=(
  "Makefile"
  "README.md"
  "docs/HELP.md"
  "build/scripts/install/install.sh"
  "build/scripts/hooks/install-hooks.sh"
)

for file in "${required_files[@]}"; do
  if [[ ! -f "$file" ]]; then
    echo "Missing required golden-path file: $file" >&2
    exit 1
  fi
done

# Ensure make wrappers point to concrete scripts.
rg -n "^install:|^check-deps:|^install-hooks:" Makefile >/dev/null
rg -n "build/scripts/install/install.sh" Makefile docs/HELP.md >/dev/null
rg -n "build/scripts/hooks/install-hooks.sh" Makefile >/dev/null

# Ensure scripts are executable.
for script in build/scripts/install/install.sh build/scripts/hooks/install-hooks.sh; do
  if [[ ! -x "$script" ]]; then
    echo "Expected executable script: $script" >&2
    exit 1
  fi
done

echo "Golden path validation passed."
