#!/usr/bin/env bash
# Claude Code PostToolUse hook: fix whitespace in a C# file the agent just edited or wrote.
#
# Reads the hook JSON payload on stdin, takes tool_input.file_path, and runs
# `dotnet format whitespace --folder` scoped to that file's directory so it neither
# loads the solution nor scans the whole repository. Parent .editorconfig files still apply.
# The hook never blocks the agent: a missing SDK, non-C# path, or formatter error exits 0.

set -uo pipefail

command -v python3 >/dev/null 2>&1 || exit 0
command -v dotnet >/dev/null 2>&1 || exit 0

file_path="$(python3 -c 'import json, sys
try:
    payload = json.load(sys.stdin)
except Exception:
    sys.exit(0)
print((payload.get("tool_input") or {}).get("file_path") or "")' 2>/dev/null)"

[[ "$file_path" == *.cs ]] || exit 0
[[ -f "$file_path" ]] || exit 0

project_dir="${CLAUDE_PROJECT_DIR:-$(git rev-parse --show-toplevel 2>/dev/null || pwd)}"
case "$(cd "$(dirname "$file_path")" && pwd -P)/" in
  "$(cd "$project_dir" && pwd -P)/"*) ;;
  *) exit 0 ;;
esac

directory="$(dirname "$file_path")"
name="$(basename "$file_path")"
if ! output="$(cd "$directory" && dotnet format whitespace . --folder --verbosity minimal --include "./$name" 2>&1)"; then
  printf 'format-csharp hook: dotnet format whitespace failed for %s\n%s\n' "$file_path" "$output" >&2
fi
exit 0
