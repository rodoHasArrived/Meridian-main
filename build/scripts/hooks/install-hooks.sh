#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
HOOKS_SRC_DIR="$ROOT_DIR/build/scripts/hooks"
GIT_HOOKS_DIR="$ROOT_DIR/.git/hooks"

if [[ ! -d "$ROOT_DIR/.git" ]]; then
  echo "ERROR: Not a git repository: $ROOT_DIR" >&2
  exit 1
fi

mkdir -p "$GIT_HOOKS_DIR"
for hook in pre-commit commit-msg; do
  src="$HOOKS_SRC_DIR/$hook"
  dst="$GIT_HOOKS_DIR/$hook"
  if [[ ! -f "$src" ]]; then
    echo "ERROR: Missing hook source: $src" >&2
    exit 1
  fi
  cp "$src" "$dst"
  chmod +x "$dst"
  echo "Installed $hook"
done

echo "Git hooks installed successfully."
