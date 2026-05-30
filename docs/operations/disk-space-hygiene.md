# Disk Space Hygiene

Use this guide when local builds, restores, or UI workflows fail because the
machine is low on space. Work from `D:\Meridian-main`.

## First Check

```powershell
Get-PSDrive -Name C
git status --short
```

Do not delete user data, source files, or untracked files that may contain user
work. In a dirty worktree, identify generated output before removing anything.

## Safe Generated Output

These directories are generated and can usually be removed after confirming
they are inside `D:\Meridian-main`:

- `bin/`
- `obj/`
- `TestResults/`
- `coverage/`
- `artifacts/publish/`
- `artifacts/install*/`
- `src/Meridian.Ui/dashboard/node_modules/`
- `src/Meridian.Ui/dashboard/dist/`
- `docs/_site/`

Use `scripts/dev/cleanup-generated.ps1` when it matches the target cleanup. For
manual cleanup on Windows, resolve absolute paths first and keep the operation
inside the repo.

## Prevent Recurrence

- Use `build/python/cli/buildctl.py build --isolation-key <name>` for concurrent
  or automation builds so outputs land under managed `artifacts/bin/` and
  `artifacts/obj/` roots with retention.
- Use `build/scripts/publish/publish.ps1 -OutputDir artifacts/publish/<name>`
  for local publish output so retention can prune old publish runs.
- Keep generated logs, screenshots, test results, and Node caches out of source
  unless a workflow explicitly tracks them as evidence.

## Higher-Risk Cleanup

Stop running `dotnet`, `node`, and Meridian processes before removing build or
publish output they may lock. Treat anything outside `D:\Meridian-main` as
machine cleanup, not repo cleanup, and document the tradeoff before touching it.
