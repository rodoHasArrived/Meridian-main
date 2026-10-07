# Git Hooks (Local Quality Gate)

Use the repository-managed pre-commit hook to check whitespace in staged C# and VB source before committing.

## Install

From the repository root:

```bash
./scripts/dev/install-git-hooks.sh
```

This configures `core.hooksPath` to `.githooks`, enabling the tracked hook scripts for this repository.
It works in ordinary checkouts and linked worktrees, where `.git` is a file. The relative hooks path
also lets each worktree use its own tracked hook version. `make install-hooks` uses this same installer;
`build/scripts/hooks/install-hooks.sh` remains a compatibility entrypoint.

## Current pre-commit checks

The hook selects added, copied, modified, and renamed staged `.cs` and `.vb` files. Commits with
no such files, including documentation-only commits and source deletions, skip .NET formatting
and do not require the .NET SDK. F#, XAML, project files, and browser source are outside this formatter's scope.

For supported source changes, the hook reads the source, `.editorconfig`, `.globalconfig`, and
`global.json` blobs from the Git index into a temporary directory, preserving their repository paths.
It then checks that snapshot with:

```bash
dotnet format whitespace . --folder --verify-no-changes --verbosity minimal --include <staged-source-paths>
```

Folder mode avoids loading or restoring the solution. A missing SDK or formatting failure blocks
source commits. The snapshot is removed afterward; the hook never formats, stashes, or stages files
in your checkout. Partially staged files are checked as they will be committed, even when their
unstaged contents differ. Staged formatting configuration is used too.

## Run the staged check manually

```bash
./.githooks/pre-commit
```

When a source check fails, the hook prints a command scoped to the affected paths. For example:

```bash
dotnet format whitespace --folder --include src/Meridian.Core/Example.cs
```

This fix command edits working files. Review the result and stage only the intended hunks again,
especially for partially staged files; the hook does not stage fixes for you.

## Full-solution validation

CI retains the full-solution whitespace check in `scripts/ci.sh`, after solution restore:

```bash
dotnet format whitespace Meridian.sln --verify-no-changes --verbosity minimal --no-restore
```

Use `dotnet format Meridian.sln` or `make format` for a broader local formatting pass.

## Hook regression tests

```bash
python3 -m unittest discover -s tests/scripts -p test_git_hooks.py -v
```

These tests exercise installation in ordinary checkouts and linked worktrees, staged source and
configuration snapshots, partial staging, filenames with spaces or newlines, and unchanged
working files and index contents on success and failure.
