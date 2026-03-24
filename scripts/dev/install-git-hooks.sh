#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

git -C "$repo_root" config core.hooksPath .githooks

echo "Configured Git hooks path to '$repo_root/.githooks'."
echo "Pre-commit will now run: dotnet format Meridian.sln --verify-no-changes"
