# Cleanup and Maintenance

**Status:** active  
**Owner:** core-team  
**Reviewed:** 2026-07-19

This page tracks recurring repository hygiene and build-safe cleanup operations.

Run cleanup commands from the repository root and preview every destructive candidate first.

## Daily Cleanup Commands

### Remove generated artifacts

Preview first:

```powershell
pwsh ./scripts/dev/cleanup-generated.ps1 -IncludeVisualStudio -IncludeNodeModules -IncludeTemp -IncludeLogs
```

Delete only after confirming no other local build, test, watcher, or desktop process is using the
workspace:

```powershell
pwsh ./scripts/dev/cleanup-generated.ps1 -IncludeVisualStudio -IncludeNodeModules -IncludeTemp -IncludeLogs -Execute
```

The script protects tracked content before deleting candidates. `-IncludeTemp` removes known
generated `.tmp` children and dashboard temp output, but intentionally avoids arbitrary PR or
worktree-looking temp folders unless they match a known generated-output pattern.

### Manual fallback (root cache folders)

Use this only when required by environment or blocked file locks, and only after resolving each path
inside the repository root:

```powershell
rm -Recurse -Force .tmp,.buildtmp,.vs,artifacts,node_modules,src/Meridian.Ui/dashboard/node_modules
```

Do not delete `artifacts/provider-validation/` when it contains intentionally retained provider
evidence. Do not delete `.claude/worktrees/` or other nested worktrees while another assistant,
GitHub Desktop, or a live validation lane owns them.

## Publish and verification hygiene

- Keep publish outputs under ignored directories (`artifacts/`, `dist/`, `publish/`) unless a release process requires promotion.
- Keep generated `coverage/`, `TestResults/`, `.tmp`/`.temp` directories ignored and uncluttered.
- Prefer immutable build products in PR-visible commands; do not commit `logs/` or `diagnostic-logs/`.
- Keep local `*.bak`, `*.backup-*`, `*.pid`, and `*.exitcode` sidecars out of source control.

## Docs, scripts, and prompt cleanup

- Review duplicate legacy guides in `docs/development`, `docs/operations`, `docs/plans`, and `docs/status`.
- Route active onboarding to:
  - [`docs/start/README.md`](../start/README.md)
  - [`docs/engineering/README.md`](../engineering/README.md)
- Keep generated or historical content in `archive/docs` and link to it from active folders.

## Cleanup classification

| Candidate | Classification | Action |
| --- | --- | --- |
| `bin/`, `obj/`, `TestResults/`, `coverage/`, `dist/`, `publish/` | Generated/build output | Delete through the cleanup script or normal build clean commands. |
| `node_modules/` | Restorable dependency cache | Delete only when no local dev server or package install is active. |
| `.tmp/`, `.buildtmp/`, `output/`, dashboard `.tmp/` | Temporary local output | Preview first; delete known generated children. |
| `artifacts/bin/`, `artifacts/obj/`, `artifacts/publish/` | Generated build/publish output | Safe cleanup targets when no validation lane is active. |
| `artifacts/provider-validation/` | Retained validation evidence | Retain unless a separate evidence-retention review says otherwise. |
| `archive/docs/`, `archive/code/` | Historical reference | Retain; update archive indexes when moving material. |
| `docs/generated/`, `docs/roadmap/generated/`, `docs/source/generated/` | Generated documentation | Do not hand edit; update inputs/generators and regenerate. |

## Locked artifact recovery

If generated files fail to delete due active process locks:

1. Identify the owning process (desktop runner, watcher, or shell session).
2. Stop the process after finishing active investigation.
3. Re-run `scripts/dev/cleanup-generated.ps1` or the manual fallback.
