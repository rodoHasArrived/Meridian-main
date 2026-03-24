# Git Hooks (Local Quality Gate)

Use the repository-managed pre-commit hook to prevent style regressions before code is committed.

## Install

From the repository root:

```bash
./scripts/dev/install-git-hooks.sh
```

This configures `core.hooksPath` to `.githooks`, enabling the tracked hook scripts for this repository.

## Current pre-commit checks

The pre-commit hook runs:

```bash
dotnet format Meridian.sln --verify-no-changes
```

If formatting issues are detected, the commit is blocked until formatting is fixed.

## Manually run the same check

```bash
dotnet format Meridian.sln --verify-no-changes
```

Or auto-fix formatting:

```bash
dotnet format Meridian.sln
```
