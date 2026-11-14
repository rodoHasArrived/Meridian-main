#!/usr/bin/env bash

set -euo pipefail

if ! command -v git >/dev/null 2>&1; then
  echo "Error: git is not installed or not on PATH." >&2
  exit 1
fi

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
hooks_dir="${repo_root}/.githooks"

for hook in pre-commit post-merge; do
  if [[ ! -f "${hooks_dir}/${hook}" ]]; then
    echo "Error: tracked ${hook} hook is missing from '${hooks_dir}'." >&2
    exit 1
  fi
  chmod +x "${hooks_dir}/${hook}"
done
git -C "$repo_root" config --local core.hooksPath .githooks

if [[ "$(git -C "$repo_root" config --get core.hooksPath)" != ".githooks" ]]; then
  echo "Failed to configure Git hooks path for '$repo_root'." >&2
  exit 1
fi

echo "Configured Git hooks path to '${repo_root}/.githooks'."
echo "Pre-commit will check staged C#/VB whitespace without changing files or the index."
echo "Commits without staged C#/VB files skip .NET formatting; CI checks the full solution."
echo "Post-merge runs buildctl.py regen --fix without staging or committing files."
