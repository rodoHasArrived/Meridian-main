# Cleanup And Maintenance

**Status:** Active
**Owner:** Core Team
**Reviewed:** 2026-05-18

Use this guide before removing files or reorganizing the repository.

## Standard Cleanup Order

1. Remove generated/build output from source folders.
2. Improve `.gitignore` so the clutter does not return.
3. Consolidate duplicate documentation into maintained indexes.
4. Consolidate scripts only after checking README, docs, CI, project files,
   package files, launch settings, and automation prompts.
5. Move useful docs-tree history to `docs/archive/` instead of deleting it.
6. Validate with the narrowest build/test command that covers the change.

## Safe Local Deletes

These are local/generated outputs and can be regenerated:

- `bin/`
- `obj/`
- `node_modules/`
- `TestResults/`
- `coverage/`
- `artifacts/install*/`
- `artifacts/publish/`
- `src/Meridian.Ui/dashboard/tsc-output.txt`
- `docs/docfx/docfx-log.json`
- `docs/docfx/temp-metadata-only.json`

Before deleting recursively on Windows, resolve the absolute path and confirm it
is inside `C:\Dev\Meridian-main`.

## Needs Review Before Delete

- `wwwroot/workstation/`: generated workstation assets are currently tracked.
- `docs/docfx/api/`: generated API metadata is currently part of the DocFX
  source path and should be regenerated or policy-reviewed before removal.
- `src/Meridian.Ui/dashboard/artifacts/automation/`: contains tracked screenshot
  evidence; prune only with an explicit evidence-retention decision.
- `.agents/`, `.codex/`, `.claude/`, and `.github/prompts/`: overlapping agent
  guidance is intentional until the AI guidance inventory is reconciled.
- `archive/`: historical reference only, but retained for traceability.
- `docs/archive/`: historical documentation retained inside the docs tree.

## Path Hygiene

Active project guidance should point to `C:\Dev\Meridian-main`. Do not update
historical notes just because they mention old paths, but fix active setup,
build, publish, and run guidance when it still references OneDrive, Desktop,
Documents, Downloads, or user-specific temporary publish locations.

Generated DocFX metadata may contain absolute source paths. Regenerate metadata
from the current checkout before publishing documentation.

## Avoiding New Duplicates

- Add short command paths to `docs/developer/`; keep long explanations in
  `docs/development/`.
- Add active architecture maps to `docs/architecture/`; archive superseded
  design notes under `docs/archive/`.
- Add prompt inventories to `docs/prompts/`; do not create a new prompt folder
  until the existing `.github/prompts/`, `.agents/`, `.codex/`, and `docs/ai/`
  surfaces have been checked.
- Keep generated reports out of source unless the docs automation contract
  explicitly tracks them.

## Validation

Use the smallest command that covers the cleanup:

```powershell
dotnet build Meridian.sln /p:EnableWindowsTargeting=true
npm --prefix src/Meridian.Ui/dashboard install
npm --prefix src/Meridian.Ui/dashboard run test
```

If `make` is not installed on Windows, use the explicit `dotnet`, `npm`, and
PowerShell commands documented in [docs/developer/build-test-run.md](../developer/build-test-run.md).
