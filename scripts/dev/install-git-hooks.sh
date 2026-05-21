#!/usr/bin/env bash

set -euo pipefail

if ! command -v git >/dev/null 2>&1; then
  echo "Error: git is not installed or not on PATH." >&2
  exit 1
fi

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
hooks_dir="${repo_root}/.githooks"

if [[ ! -d "${hooks_dir}" ]]; then
  mkdir -p "${hooks_dir}"
  echo "Created hooks directory at '${hooks_dir}'."
fi

git -C "$repo_root" config core.hooksPath .githooks

if [[ "$(git -C "$repo_root" config --get core.hooksPath)" != ".githooks" ]]; then
  echo "Failed to configure Git hooks path for '$repo_root'." >&2
  exit 1
fi

echo "Configured Git hooks path to '${repo_root}/.githooks'."
echo "Pre-commit will now run: dotnet format Meridian.sln --verify-no-changes"
