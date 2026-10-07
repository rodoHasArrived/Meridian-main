#!/usr/bin/env bash
set -euo pipefail

# Compatibility entrypoint: core.hooksPath also works when .git is a worktree file.
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
exec "$repo_root/scripts/dev/install-git-hooks.sh" "$@"
