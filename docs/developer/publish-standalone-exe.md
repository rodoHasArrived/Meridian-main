# Publishing Standalone Executable (Current)

**Status:** active  
**Owner:** core-team  
**Reviewed:** 2026-06-14

Use this page for standalone publish artifacts used by local validation and release workflows.

Current local project path: `D:\Meridian-main`.

## Scripted Publish (Preferred)

```powershell
pwsh ./build/scripts/publish/publish.ps1 `
  -Project desktop `
  -Platform win-x64 `
  -Version 1.0.0 `
  -Configuration Release `
  -OutputDir artifacts/publish/local-desktop
```

Other projects:

- `-Project collector` for the CLI host (`src/Meridian/Meridian.csproj`)
- `-Project web-workstation` for hosted workstation bundle flows
- `-Platform osx-x64`, `osx-arm64`, `linux-x64`, `linux-arm64` for non-Windows outputs

## CI Publish (Windows Standalone)

```powershell
pwsh ./build/scripts/publish/publish.ps1 -Platform win-x64 -Project desktop -OutputDir artifacts/publish/local-desktop
```

The workflow wrapper is [`desktop-standalone-publish.yml`](../../.github/workflows/desktop-standalone-publish.yml).

## Output Layout

- Default artifact output is `./dist` unless `-OutputDir` is provided.
- Desktop release outputs are conventionally produced under:
  - `artifacts/publish/local-desktop/win-x64/desktop/`

## Maintenance Notes

- Clean old publish artifacts with the workspace cleanup instructions in [`docs/operations/cleanup-and-maintenance.md`](../operations/cleanup-and-maintenance.md).
- Keep output roots ignored in `.gitignore` and avoid committing binary payloads.
