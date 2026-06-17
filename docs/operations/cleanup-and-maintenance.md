# Cleanup and Maintenance

**Status:** active  
**Owner:** core-team  
**Reviewed:** 2026-06-14

This page tracks recurring repository hygiene and build-safe cleanup operations.

Current local project path: `D:\Meridian-main`.

## Daily Cleanup Commands

### Remove generated artifacts

```powershell
pwsh ./scripts/dev/cleanup-generated.ps1 -IncludeVisualStudio -IncludeNodeModules -IncludeTemp -IncludeLogs
pwsh ./scripts/dev/cleanup-generated.ps1 -IncludeVisualStudio -IncludeNodeModules -IncludeTemp -IncludeLogs -Execute
```

### Manual fallback (root cache folders)

Use this only when required by environment or blocked file locks:

```powershell
rm -Recurse -Force .tmp,.buildtmp,.vs,artifacts,node_modules,src/Meridian.Ui/dashboard/node_modules
```

## Publish and verification hygiene

- Keep publish outputs under ignored directories (`artifacts/`, `dist/`, `publish/`) unless a release process requires promotion.
- Keep generated `coverage/`, `TestResults/`, `.tmp`/`.temp` directories ignored and uncluttered.
- Prefer immutable build products in PR-visible commands; do not commit `logs/` or `diagnostic-logs/`.

## Docs, scripts, and prompt cleanup

- Review duplicate legacy guides in `docs/development`, `docs/operations`, `docs/plans`, and `docs/status`.
- Route active onboarding to:
  - [`docs/start/README.md`](../start/README.md)
  - [`docs/developer/setup.md`](../developer/setup.md)
  - [`docs/engineering/README.md`](../engineering/README.md)
- Keep generated or historical content in `archive/docs` and link to it from active folders.

## Locked artifact recovery

If generated files fail to delete due active process locks:

1. Identify the owning process (desktop runner, watcher, or shell session).
2. Stop the process after finishing active investigation.
3. Re-run `scripts/dev/cleanup-generated.ps1` or the manual fallback.
