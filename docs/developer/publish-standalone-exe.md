# Publish Standalone EXE

**Status:** Active
**Owner:** Core Team
**Reviewed:** 2026-05-18

Use the repository publish script for local standalone executable output. Run it
from `C:\Dev\Meridian-main`.

```powershell
pwsh ./build/scripts/publish/publish.ps1 -Platform win-x64 -Project collector -OutputDir artifacts/publish/local-standalone
```

The publish script writes generated output under `artifacts/publish/`, which is
ignored by Git and should not be committed.

The Windows collector executable is written under:

```text
artifacts/publish/local-standalone/win-x64/collector/Meridian.exe
```

For the retained WPF desktop executable:

```powershell
pwsh ./build/scripts/publish/publish.ps1 -Platform win-x64 -Project desktop -OutputDir artifacts/publish/local-desktop
```

The manual `Publish Smoke` GitHub Actions workflow runs the same script on a
Windows runner and uploads the generated `artifacts/publish/publish-smoke/`
directory. It does not create a public release or deploy externally.

For the browser workstation local app installer, use:

```powershell
pwsh ./build/scripts/install/install.ps1 -Mode WebWorkstation -SkipInstall
```

For retained desktop MSIX packaging, use
[docs/operations/msix-packaging.md](../operations/msix-packaging.md). For the
browser workstation installer, use
[docs/operations/web-workstation-installer.md](../operations/web-workstation-installer.md).
